using System;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using Il2CppInterop.Runtime.Attributes;
using Just_Lilith.Core.Threading;
using UnityEngine;

namespace Just_Lilith.Unity;

public sealed class MainThreadPump(IntPtr pointer) : MonoBehaviour(pointer)
{
	private readonly TaskCompletionSource<IMainThreadDispatcher> _ready = new TaskCompletionSource<IMainThreadDispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);

	private readonly object _lifecycleGate = new object();

	private MainThreadDispatcher? _dispatcher;

	private ManualLogSource? _log;

	private Task? _readyNotification;

	private int _stopRequested;

	private bool _destroyRequested;

	public void Awake()
	{
		GetOrCreateDispatcherFromUnityCallback();
	}

	[HideFromIl2Cpp]
	public void Configure(ManualLogSource logger)
	{
		ArgumentNullException.ThrowIfNull(logger, "logger");
		if (_log != null)
		{
			throw new InvalidOperationException("Main-thread component already configured.");
		}
		_log = logger;
	}

	[HideFromIl2Cpp]
	public Task<IMainThreadDispatcher> GetReadyTask()
	{
		return _ready.Task;
	}

	public void Update()
	{
		if (Volatile.Read(ref _stopRequested) != 0)
		{
			if (!_destroyRequested)
			{
				_destroyRequested = true;
				UnityEngine.Object.Destroy(this);
			}
			return;
		}
		try
		{
			MainThreadDispatcher dispatcher = GetOrCreateDispatcherFromUnityCallback();
			if (dispatcher == null || _log == null)
			{
				return;
			}
			if (_readyNotification == null)
			{
				_readyNotification = dispatcher.Post(delegate
				{
					if (Volatile.Read(ref _stopRequested) == 0)
					{
						ManualLogSource? log2 = _log;
						BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(90, 2, out var isEnabled2);
						if (isEnabled2)
						{
							bepInExInfoLogInterpolatedStringHandler.AppendLiteral("module=MainThread; state=ready; callback=Update; thread=");
							bepInExInfoLogInterpolatedStringHandler.AppendFormatted(Environment.CurrentManagedThreadId);
							bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; frame=");
							bepInExInfoLogInterpolatedStringHandler.AppendFormatted(Time.frameCount);
							bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; queue_dispatch=confirmed");
						}
						log2.LogInfo(bepInExInfoLogInterpolatedStringHandler);
						_ready.TrySetResult(dispatcher);
					}
				});
			}
			dispatcher.Pump();
			if (!_readyNotification.IsFaulted)
			{
				return;
			}
			throw _readyNotification.Exception.GetBaseException();
		}
		catch (Exception) when (Volatile.Read(ref _stopRequested) != 0)
		{
		}
		catch (Exception ex2)
		{
			_ready.TrySetException(ex2);
			ManualLogSource log = _log;
			if (log != null)
			{
				BepInExErrorLogInterpolatedStringHandler bepInExErrorLogInterpolatedStringHandler = new BepInExErrorLogInterpolatedStringHandler(39, 1, out var isEnabled);
				if (isEnabled)
				{
					bepInExErrorLogInterpolatedStringHandler.AppendLiteral("module=MainThread; state=failed; error=");
					bepInExErrorLogInterpolatedStringHandler.AppendFormatted(ex2);
				}
				log.LogError(bepInExErrorLogInterpolatedStringHandler);
			}
			RequestStop("callback_failure");
		}
	}

	[HideFromIl2Cpp]
	private MainThreadDispatcher? GetOrCreateDispatcherFromUnityCallback()
	{
		lock (_lifecycleGate)
		{
			if (Volatile.Read(ref _stopRequested) != 0)
			{
				return null;
			}
			return _dispatcher ?? (_dispatcher = new MainThreadDispatcher());
		}
	}

	[HideFromIl2Cpp]
	public void RequestStop(string reason)
	{
		if (Interlocked.Exchange(ref _stopRequested, 1) != 0)
		{
			return;
		}
		lock (_lifecycleGate)
		{
			_dispatcher?.Dispose();
		}
		_ready.TrySetCanceled();
		ManualLogSource log = _log;
		if (log != null)
		{
			BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(65, 1, out var isEnabled);
			if (isEnabled)
			{
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("module=MainThread; state=stopped; reason=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(reason);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; pending_cancelled=true");
			}
			log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
		}
	}

	public void OnApplicationQuit()
	{
		RequestStop("application_quit");
	}

	public void OnDestroy()
	{
		RequestStop("component_destroyed");
	}
}
