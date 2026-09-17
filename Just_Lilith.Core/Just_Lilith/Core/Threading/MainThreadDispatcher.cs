using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Just_Lilith.Core.Threading;

public sealed class MainThreadDispatcher : IMainThreadDispatcher, IDisposable
{
	private sealed class WorkItem
	{
		private const int Pending = 0;

		private const int Running = 1;

		private const int Terminal = 2;

		private readonly Action _action;

		private readonly CancellationToken _token;

		private readonly TaskCompletionSource<object?> _completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

		private readonly CancellationTokenRegistration _registration;

		private int _state;

		public Task Completion => _completion.Task;

		public WorkItem(Action action, CancellationToken token)
		{
			WorkItem workItem = this;
			_action = action;
			_token = token;
			_registration = token.Register(delegate
			{
				workItem.CancelPending(token);
			});
		}

		public bool TryStart()
		{
			if (_token.IsCancellationRequested)
			{
				CancelPending(_token);
			}
			return Interlocked.CompareExchange(ref _state, 1, 0) == 0;
		}

		public void RunOrRelease(bool started)
		{
			try
			{
				if (!started)
				{
					return;
				}
				try
				{
					_action();
					_completion.TrySetResult(null);
				}
				catch (Exception exception)
				{
					_completion.TrySetException(exception);
				}
				finally
				{
					Volatile.Write(ref _state, 2);
				}
			}
			finally
			{
				_registration.Dispose();
			}
		}

		public void Cancel()
		{
			CancelPending(default(CancellationToken));
			_registration.Dispose();
		}

		private void CancelPending(CancellationToken token)
		{
			if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
			{
				_completion.TrySetCanceled(token);
			}
		}
	}

	private const int Capacity = 256;

	private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;

	private readonly object _gate = new object();

	private readonly Queue<WorkItem> _pending = new Queue<WorkItem>();

	private bool _disposed;

	public bool IsMainThread => Environment.CurrentManagedThreadId == _ownerThreadId;

	public Task Post(Action action, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(action, "action");
		lock (_gate)
		{
			if (_disposed)
			{
				throw new ObjectDisposedException("MainThreadDispatcher");
			}
			if (cancellationToken.IsCancellationRequested)
			{
				return Task.FromCanceled(cancellationToken);
			}
			if (_pending.Count >= 256)
			{
				throw new InvalidOperationException("Main-thread queue is full.");
			}
			WorkItem workItem = new WorkItem(action, cancellationToken);
			_pending.Enqueue(workItem);
			return workItem.Completion;
		}
	}

	public int Pump(int maximumItems = 32)
	{
		if (!IsMainThread)
		{
			throw new InvalidOperationException("Only the owning main thread may drain the queue.");
		}
		if (maximumItems < 1)
		{
			throw new ArgumentOutOfRangeException("maximumItems");
		}
		int i;
		WorkItem workItem;
		bool started;
		for (i = 0; i < maximumItems; workItem.RunOrRelease(started), i++)
		{
			lock (_gate)
			{
				if (_disposed || _pending.Count == 0)
				{
					break;
				}
				workItem = _pending.Dequeue();
				started = workItem.TryStart();
				continue;
			}
		}
		return i;
	}

	public void Dispose()
	{
		WorkItem[] array;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}
			_disposed = true;
			array = _pending.ToArray();
			_pending.Clear();
		}
		WorkItem[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			array2[i].Cancel();
		}
	}
}
