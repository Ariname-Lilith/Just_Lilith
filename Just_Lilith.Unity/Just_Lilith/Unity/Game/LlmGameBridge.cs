using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using Just_Lilith.Core.Llm;
using Just_Lilith.Sync;
using UnityEngine;

namespace Just_Lilith.Unity.Game;

internal sealed class LlmGameBridge : IOrdinaryChatOutputSink, IThinkingBubbleSink, IDisposable
{
	private sealed class PendingBubble
	{
		public OrdinaryChatOutput Output { get; }

		public DateTimeOffset EnqueuedAt { get; }

		public int NonBusyRejections { get; set; }

		public bool SpeechSuppressed { get; set; }

		public PendingBubble(OrdinaryChatOutput output, DateTimeOffset enqueuedAt)
		{
			Output = output;
			EnqueuedAt = enqueuedAt;
		}
	}

	private const int BubbleQueueLimit = 8;

	private const int TtsQueueLimit = 16;

	private readonly ManualLogSource _log;

	private readonly Queue<PendingBubble> _bubbleQueue = new Queue<PendingBubble>();

	private readonly Queue<OrdinaryChatOutput> _ttsQueue = new Queue<OrdinaryChatOutput>();

	private readonly object _messageGate = new object();

	private object? _dialogueManager;

	private RuntimeValueAccessor? _busyValue;

	private RuntimeValueAccessor? _dialogueActiveValue;

	private RuntimeValueAccessor? _currentNodeValue;

	private RuntimeValueAccessor? _currentNodeTextValue;

	private MethodInfo? _forceSay;

	private MethodInfo? _resetAutoDismiss;

	private MethodInfo? _forceEndDialogue;

	private MethodInfo? _isBubbleTyping;

	private float _nextResolveAt;

	private float _nextAttemptAt;

	private bool _thinkingRequested;

	private bool _thinkingShown;

	private float _thinkingShownAt;

	private float _nextThinkingAt;

	private string? _lastShownText;

	private bool _replyHoldActive;

	private bool _replyTypingClosed;

	private float _replyHoldStartedAt;

	private float _replyTypingObserved;

	private float _replyTypingFloor;

	private float _replyHoldSeconds;

	private float _nextKeepAliveAt;

	private bool _readyLogged;

	private bool _unavailableLogged;

	private string _lastDeliveryStatus = "尚无回复";

	private int _disposed;

	public string DeliveryStatus
	{
		get
		{
			lock (_messageGate)
			{
				return (_thinkingRequested ? "思考中 · " : "") + $"气泡等待 {_bubbleQueue.Count} · {_lastDeliveryStatus} · TTS 等待 {_ttsQueue.Count}";
			}
		}
	}

	public LlmGameBridge(ManualLogSource log)
	{
		_log = log;
	}

	public void ShowThinking()
	{
		if (Volatile.Read(ref _disposed) != 0)
		{
			return;
		}
		lock (_messageGate)
		{
			_thinkingRequested = true;
			_lastDeliveryStatus = "用户输入已提交，等待模型输出";
		}
		_thinkingShown = false;
		_thinkingShownAt = 0f;
		_nextThinkingAt = 0f;
		_replyHoldActive = false;
		_log.LogInfo("module=GameBridge; event=thinking_requested; placeholder=" + BubblePresentationTiming.ThinkingText);
	}

	public void EndThinking()
	{
		lock (_messageGate)
		{
			_thinkingRequested = false;
		}
	}

	public void Publish(OrdinaryChatOutput output)
	{
		ArgumentNullException.ThrowIfNull(output, "output");
		if (Volatile.Read(ref _disposed) != 0)
		{
			throw new ObjectDisposedException("LlmGameBridge");
		}
		PendingBubble pendingBubble = null;
		lock (_messageGate)
		{
			if (_bubbleQueue.Count >= 8)
			{
				pendingBubble = _bubbleQueue.Dequeue();
				RemoveTtsMessageNoLock(pendingBubble.Output.MessageId);
			}
			_bubbleQueue.Enqueue(new PendingBubble(output, DateTimeOffset.UtcNow));
			BubbleSpeechSync.EnqueueSpeech(this, output);
			_lastDeliveryStatus = "等待气泡 " + MessageTag(output.MessageId);
		}
		bool isEnabled;
		if (pendingBubble != null)
		{
			ManualLogSource log = _log;
			BepInExWarningLogInterpolatedStringHandler bepInExWarningLogInterpolatedStringHandler = new BepInExWarningLogInterpolatedStringHandler(78, 1, out isEnabled);
			if (isEnabled)
			{
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral("module=GameBridge; event=bubble_queue_trimmed; message_id=");
				bepInExWarningLogInterpolatedStringHandler.AppendFormatted(pendingBubble.Output.MessageId);
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral("; tts_eligible=false");
			}
			log.LogWarning(bepInExWarningLogInterpolatedStringHandler);
		}
		ManualLogSource log2 = _log;
		BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(97, 1, out isEnabled);
		if (isEnabled)
		{
			bepInExInfoLogInterpolatedStringHandler.AppendLiteral("module=GameBridge; event=reply_queued; message_id=");
			bepInExInfoLogInterpolatedStringHandler.AppendFormatted(output.MessageId);
			bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; bubble=estimated_delay; tts_text=independent");
		}
		log2.LogInfo(bepInExInfoLogInterpolatedStringHandler);
	}

	public bool TryDequeueTtsText(out OrdinaryChatOutput output)
	{
		lock (_messageGate)
		{
			if (_ttsQueue.Count == 0)
			{
				output = null;
				return false;
			}
			output = _ttsQueue.Dequeue();
			return true;
		}
	}

	public void ClearPendingSpeech()
	{
		lock (_messageGate)
		{
			_ttsQueue.Clear();
			foreach (PendingBubble item in _bubbleQueue)
			{
				item.SpeechSuppressed = true;
			}
		}
	}

	public void Update()
	{
		if (Volatile.Read(ref _disposed) != 0)
		{
			return;
		}
		UpdateThinkingBubble();
		UpdateReplyHold();
		List<PendingBubble> list = new List<PendingBubble>();
		PendingBubble pendingBubble2;
		lock (_messageGate)
		{
			DateTimeOffset utcNow = DateTimeOffset.UtcNow;
			while (_bubbleQueue.Count > 0 && BubbleDeliveryRetryPolicy.HasExpired(_bubbleQueue.Peek().EnqueuedAt, utcNow))
			{
				PendingBubble pendingBubble = _bubbleQueue.Dequeue();
				RemoveTtsMessageNoLock(pendingBubble.Output.MessageId);
				list.Add(pendingBubble);
				_lastDeliveryStatus = "气泡超时 " + MessageTag(pendingBubble.Output.MessageId);
			}
			if (_bubbleQueue.Count == 0 || Time.unscaledTime < _nextAttemptAt)
			{
				pendingBubble2 = null;
			}
			else if (!BubbleSpeechSync.IsReady(_bubbleQueue.Peek().EnqueuedAt, utcNow, _bubbleQueue.Peek().Output) || !IsThinkingHoldElapsed())
			{
				pendingBubble2 = null;
			}
			else
			{
				_nextAttemptAt = Time.unscaledTime + (float)BubbleDeliveryRetryPolicy.RetryInterval.TotalSeconds;
				pendingBubble2 = _bubbleQueue.Peek();
			}
		}
		bool isEnabled;
		foreach (PendingBubble item in list)
		{
			ManualLogSource log = _log;
			BepInExWarningLogInterpolatedStringHandler bepInExWarningLogInterpolatedStringHandler = new BepInExWarningLogInterpolatedStringHandler(113, 3, out isEnabled);
			if (isEnabled)
			{
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral("module=GameBridge; event=bubble_expired; message_id=");
				bepInExWarningLogInterpolatedStringHandler.AppendFormatted(item.Output.MessageId);
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral("; ");
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral("max_wait_seconds=");
				bepInExWarningLogInterpolatedStringHandler.AppendFormatted(BubbleDeliveryRetryPolicy.MaximumWait.TotalSeconds, "0");
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral("; ");
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral("non_busy_rejections=");
				bepInExWarningLogInterpolatedStringHandler.AppendFormatted(item.NonBusyRejections);
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral("; tts_eligible=false");
			}
			log.LogWarning(bepInExWarningLogInterpolatedStringHandler);
		}
		if (pendingBubble2 == null)
		{
			ResolveDialogueManager();
		}
		else
		{
			if (!ResolveDialogueManager())
			{
				return;
			}
			try
			{
				object obj;
				if (_dialogueManager != null && !IsOwnedBubbleCurrent())
				{
					obj = _busyValue?.GetValue(_dialogueManager);
					if (obj is bool && (bool)obj)
					{
						return;
					}
				}
				string text = EscapeRichText(pendingBubble2.Output.DisplayText);
				_ = pendingBubble2.Output.DisplayText;
				_ = pendingBubble2.Output.TtsText;
				_ = pendingBubble2.Output.SpeechLanguage;
				_ = pendingBubble2.Output.SpeechEnabled;
				BubblePresentationSchedule timing = BubbleSpeechSync.Plan(pendingBubble2.Output);
				float num = (float)BubblePresentationTiming.KeepAliveNodeDuration.TotalSeconds;
				obj = _forceSay?.Invoke(_dialogueManager, new object[3] { text, "", num });
				if (obj is bool && (bool)obj)
				{
					CompleteAccepted(pendingBubble2, timing);
				}
				else
				{
					RecordRetryableRejection(pendingBubble2);
				}
			}
			catch (TargetInvocationException ex)
			{
				InvalidateManager();
				ManualLogSource log2 = _log;
				BepInExWarningLogInterpolatedStringHandler bepInExWarningLogInterpolatedStringHandler = new BepInExWarningLogInterpolatedStringHandler(66, 2, out isEnabled);
				if (isEnabled)
				{
					bepInExWarningLogInterpolatedStringHandler.AppendLiteral("module=GameBridge; event=bubble_display_failed; message_id=");
					bepInExWarningLogInterpolatedStringHandler.AppendFormatted(pendingBubble2.Output.MessageId);
					bepInExWarningLogInterpolatedStringHandler.AppendLiteral("; code=");
					bepInExWarningLogInterpolatedStringHandler.AppendFormatted(ex.InnerException?.GetType().Name ?? ex.GetType().Name);
				}
				log2.LogWarning(bepInExWarningLogInterpolatedStringHandler);
			}
			catch (Exception ex2)
			{
				InvalidateManager();
				ManualLogSource log3 = _log;
				BepInExWarningLogInterpolatedStringHandler bepInExWarningLogInterpolatedStringHandler = new BepInExWarningLogInterpolatedStringHandler(66, 2, out isEnabled);
				if (isEnabled)
				{
					bepInExWarningLogInterpolatedStringHandler.AppendLiteral("module=GameBridge; event=bubble_display_failed; message_id=");
					bepInExWarningLogInterpolatedStringHandler.AppendFormatted(pendingBubble2.Output.MessageId);
					bepInExWarningLogInterpolatedStringHandler.AppendLiteral("; code=");
					bepInExWarningLogInterpolatedStringHandler.AppendFormatted(ex2.GetType().Name);
				}
				log3.LogWarning(bepInExWarningLogInterpolatedStringHandler);
			}
		}
	}

	private void CompleteAccepted(PendingBubble accepted, BubblePresentationSchedule timing)
	{
		OrdinaryChatOutput ordinaryChatOutput = null;
		bool flag = false;
		lock (_messageGate)
		{
			if (_bubbleQueue.Count == 0 || _bubbleQueue.Peek() != accepted)
			{
				return;
			}
			_bubbleQueue.Dequeue();
			_thinkingRequested = false;
			_lastDeliveryStatus = "气泡已显示 " + MessageTag(accepted.Output.MessageId);
			flag = true;
		}
		_thinkingShown = false;
		_thinkingShownAt = 0f;
		_lastShownText = EscapeRichText(accepted.Output.DisplayText);
		_replyHoldActive = true;
		_replyTypingClosed = false;
		_replyHoldStartedAt = Time.unscaledTime;
		_replyTypingObserved = 0f;
		_replyTypingFloor = (float)BubblePresentationTiming.PlanTypingDuration(accepted.Output.DisplayText).TotalSeconds;
		_replyHoldSeconds = (float)timing.HoldDuration.TotalSeconds;
		_nextKeepAliveAt = 0f;
		bool isEnabled;
		if ((object)ordinaryChatOutput != null)
		{
			ManualLogSource log = _log;
			BepInExWarningLogInterpolatedStringHandler bepInExWarningLogInterpolatedStringHandler = new BepInExWarningLogInterpolatedStringHandler(80, 1, out isEnabled);
			if (isEnabled)
			{
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral("module=GameBridge; event=tts_queue_trimmed; message_id=");
				bepInExWarningLogInterpolatedStringHandler.AppendFormatted(ordinaryChatOutput.MessageId);
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral("; bubble=already_accepted");
			}
			log.LogWarning(bepInExWarningLogInterpolatedStringHandler);
		}
		if (flag)
		{
			ManualLogSource log2 = _log;
			BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(140, 3, out isEnabled);
			if (isEnabled)
			{
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("module=GameBridge; event=bubble_display_accepted; message_id=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(accepted.Output.MessageId);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; ");
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("source=DialogueManager.ForceSay; delay_ms=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(timing.Delay.TotalMilliseconds, "0");
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; ");
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("duration_seconds=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(timing.VisibleDuration.TotalSeconds, "0.00");
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; tts_text=ready");
			}
			log2.LogInfo(bepInExInfoLogInterpolatedStringHandler);
		}
	}

	private void RecordRetryableRejection(PendingBubble rejected)
	{
		int num = 0;
		lock (_messageGate)
		{
			if (_bubbleQueue.Count == 0 || _bubbleQueue.Peek() != rejected)
			{
				return;
			}
			num = ++rejected.NonBusyRejections;
			_lastDeliveryStatus = $"气泡等待重试 {MessageTag(rejected.Output.MessageId)}（拒绝 {num} 次）";
		}
		if (num == 1)
		{
			_log.LogInfo($"module=GameBridge; event=bubble_retrying; message_id={rejected.Output.MessageId}; " + "reason=ForceSay_returned_false; terminal=false");
		}
	}

	private void UpdateThinkingBubble()
	{
		bool flag;
		lock (_messageGate)
		{
			flag = _thinkingRequested || _bubbleQueue.Count > 0;
		}
		if (!flag)
		{
			HideThinkingBubble();
			return;
		}
		if (Time.unscaledTime < _nextThinkingAt)
		{
			return;
		}
		if (!ResolveDialogueManager())
		{
			return;
		}
		_nextThinkingAt = Time.unscaledTime + (float)BubblePresentationTiming.KeepAliveInterval.TotalSeconds;
		if (IsThinkingBubbleCurrent())
		{
			KeepAliveBubble();
			return;
		}
		if (IsDialogueActive() && !IsOwnedBubbleCurrent())
		{
			// 角色正在播放自己的台词或状态：先把气泡留给它，稍后再接管。
			_thinkingShown = false;
			return;
		}
		bool isEnabled;
		bool flag2;
		try
		{
			flag2 = _forceSay?.Invoke(_dialogueManager, new object[3]
			{
				EscapeRichText(BubblePresentationTiming.ThinkingText),
				"",
				(float)BubblePresentationTiming.KeepAliveNodeDuration.TotalSeconds
			}) is bool flag3 && flag3;
		}
		catch (TargetInvocationException)
		{
			InvalidateManager();
			flag2 = false;
		}
		catch (Exception)
		{
			InvalidateManager();
			flag2 = false;
		}
		_thinkingShown = flag2;
		if (flag2)
		{
			_thinkingShownAt = Time.unscaledTime;
			ManualLogSource log = _log;
			BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(96, 1, out isEnabled);
			if (isEnabled)
			{
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("module=GameBridge; event=thinking_shown; duration_seconds=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(BubblePresentationTiming.KeepAliveNodeDuration.TotalSeconds, "0.00");
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; source=DialogueManager.ForceSay");
			}
			log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
		}
	}

	private void KeepAliveBubble()
	{
		try
		{
			_resetAutoDismiss?.Invoke(_dialogueManager, Array.Empty<object>());
		}
		catch (Exception)
		{
			InvalidateManager();
		}
	}

	private void HideThinkingBubble()
	{
		if (!_thinkingShown)
		{
			return;
		}
		_thinkingShown = false;
		_thinkingShownAt = 0f;
		if (!IsThinkingBubbleCurrent())
		{
			return;
		}
		try
		{
			_forceEndDialogue?.Invoke(_dialogueManager, Array.Empty<object>());
			_log.LogInfo("module=GameBridge; event=thinking_hidden; reason=request_finished");
		}
		catch (Exception)
		{
			InvalidateManager();
		}
	}

	/// <summary>
	/// 游戏在逐字显示期间会把自动关闭计时压到 1 秒并暂停倒数，文字显示完成后约 1 秒就收起气泡，
	/// 调用方传入的 duration 会被这一步覆盖。这里改为由插件托管：文字仍在逐字显示时持续续期，
	/// 显示完成后按字数继续停留，到期后不再续期，交给游戏自己的收起流程收尾。
	/// </summary>
	private void UpdateReplyHold()
	{
		if (!_replyHoldActive)
		{
			return;
		}
		if (Time.unscaledTime < _nextKeepAliveAt)
		{
			return;
		}
		_nextKeepAliveAt = Time.unscaledTime + (float)BubblePresentationTiming.KeepAliveInterval.TotalSeconds;
		if (!IsOwnedReplyBubbleCurrent())
		{
			_replyHoldActive = false;
			return;
		}
		float num = Time.unscaledTime - _replyHoldStartedAt;
		if (!_replyTypingClosed)
		{
			if (IsBubbleTyping() && num < (float)BubblePresentationTiming.MaximumTypingEstimate.TotalSeconds)
			{
				_replyTypingObserved = num;
				KeepAliveBubble();
				return;
			}
			_replyTypingClosed = true;
		}
		float num2 = Math.Max(_replyTypingObserved, _replyTypingFloor) + _replyHoldSeconds;
		num2 = Math.Min(num2, (float)BubblePresentationTiming.MaximumTotalVisible.TotalSeconds);
		if (num < num2)
		{
			KeepAliveBubble();
			return;
		}
		_replyHoldActive = false;
		bool isEnabled;
		ManualLogSource log = _log;
		BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(124, 3, out isEnabled);
		if (isEnabled)
		{
			bepInExInfoLogInterpolatedStringHandler.AppendLiteral("module=GameBridge; event=bubble_hold_finished; visible_seconds=");
			bepInExInfoLogInterpolatedStringHandler.AppendFormatted(num, "0.00");
			bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; typing_seconds=");
			bepInExInfoLogInterpolatedStringHandler.AppendFormatted(Math.Max(_replyTypingObserved, _replyTypingFloor), "0.00");
			bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; reading_seconds=");
			bepInExInfoLogInterpolatedStringHandler.AppendFormatted(_replyHoldSeconds, "0.00");
		}
		log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
	}

	private bool IsBubbleTyping()
	{
		if (_dialogueManager == null || _isBubbleTyping == null)
		{
			return false;
		}
		try
		{
			return _isBubbleTyping.Invoke(_dialogueManager, Array.Empty<object>()) is bool flag && flag;
		}
		catch (Exception)
		{
			return false;
		}
	}

	private bool IsOwnedReplyBubbleCurrent()
	{
		return _lastShownText != null && TryReadCurrentBubbleText(out string text) && string.Equals(text, _lastShownText, StringComparison.Ordinal);
	}

	private bool IsThinkingHoldElapsed()
	{
		if (!_thinkingShown || _thinkingShownAt <= 0f)
		{
			return true;
		}
		return (double)(Time.unscaledTime - _thinkingShownAt) >= BubblePresentationTiming.ThinkingMinimumVisible.TotalSeconds;
	}

	private bool IsThinkingBubbleCurrent()
	{
		return TryReadCurrentBubbleText(out string text) && string.Equals(text, BubblePresentationTiming.ThinkingText, StringComparison.Ordinal);
	}

	private bool IsOwnedBubbleCurrent()
	{
		if (!TryReadCurrentBubbleText(out string text))
		{
			return false;
		}
		if (string.Equals(text, BubblePresentationTiming.ThinkingText, StringComparison.Ordinal))
		{
			return true;
		}
		return _lastShownText != null && string.Equals(text, _lastShownText, StringComparison.Ordinal);
	}

	private bool TryReadCurrentBubbleText(out string? text)
	{
		text = null;
		if (_dialogueManager == null || _currentNodeValue == null || !IsDialogueActive())
		{
			return false;
		}
		try
		{
			object obj = _currentNodeValue.GetValue(_dialogueManager);
			if (obj == null)
			{
				return false;
			}
			if (_currentNodeTextValue == null)
			{
				_currentNodeTextValue = RuntimeValueAccessor.Find(obj.GetType(), "text");
			}
			text = _currentNodeTextValue?.GetValue(obj) as string;
			return !string.IsNullOrEmpty(text);
		}
		catch (Exception)
		{
			return false;
		}
	}

	private bool IsDialogueActive()
	{
		if (_dialogueManager == null || _dialogueActiveValue == null)
		{
			return false;
		}
		try
		{
			return _dialogueActiveValue.GetValue(_dialogueManager) is bool flag && flag;
		}
		catch (Exception)
		{
			InvalidateManager();
			return false;
		}
	}

	private bool ResolveDialogueManager()
	{
		if (_dialogueManager is UnityEngine.Object obj && obj != null && (object)_forceSay != null)
		{
			return true;
		}
		if (Time.unscaledTime < _nextResolveAt)
		{
			return false;
		}
		_nextResolveAt = Time.unscaledTime + 2f;
		bool isEnabled;
		try
		{
			if (!UnityRuntimeObjectLocator.TryFind("DialogueManager, Assembly-CSharp", out var located))
			{
				LogWaitingForManager();
				return false;
			}
			MethodInfo method = located.ReflectedType.GetMethod("ForceSay", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[3]
			{
				typeof(string),
				typeof(string),
				typeof(float)
			}, null);
			RuntimeValueAccessor busyValue = RuntimeValueAccessor.Find(located.ReflectedType, "IsBusyOrAwaitingResponse") ?? RuntimeValueAccessor.Find(located.ReflectedType, "IsBusy") ?? RuntimeValueAccessor.Find(located.ReflectedType, "IsDialogueActive");
			if ((object)method == null || method.ReturnType != typeof(bool))
			{
				LogWaitingForManager();
				return false;
			}
			_dialogueManager = located.Instance;
			_forceSay = method;
			_busyValue = busyValue;
			_dialogueActiveValue = RuntimeValueAccessor.Find(located.ReflectedType, "IsDialogueActive");
			_currentNodeValue = RuntimeValueAccessor.Find(located.ReflectedType, "CurrentNode");
			_currentNodeTextValue = null;
			_resetAutoDismiss = located.ReflectedType.GetMethod("ResetAutoDismissTimer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
			_forceEndDialogue = located.ReflectedType.GetMethod("ForceEndDialogue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
			_isBubbleTyping = located.ReflectedType.GetMethod("IsBubbleTyping", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
			_unavailableLogged = false;
			if (!_readyLogged)
			{
				_readyLogged = true;
				ManualLogSource log = _log;
				BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(102, 1, out isEnabled);
				if (isEnabled)
				{
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("module=GameBridge; state=ready; locator=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(located.Strategy);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; bubble=DialogueManager.ForceSay; tts=text_queue_after_bubble");
				}
				log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
			}
			return true;
		}
		catch (Exception ex)
		{
			if (!_unavailableLogged)
			{
				_unavailableLogged = true;
				ManualLogSource log2 = _log;
				BepInExWarningLogInterpolatedStringHandler bepInExWarningLogInterpolatedStringHandler = new BepInExWarningLogInterpolatedStringHandler(43, 1, out isEnabled);
				if (isEnabled)
				{
					bepInExWarningLogInterpolatedStringHandler.AppendLiteral("module=GameBridge; state=unavailable; code=");
					bepInExWarningLogInterpolatedStringHandler.AppendFormatted(ex.GetType().Name);
				}
				log2.LogWarning(bepInExWarningLogInterpolatedStringHandler);
			}
			return false;
		}
	}

	private void LogWaitingForManager()
	{
		if (!_unavailableLogged)
		{
			_unavailableLogged = true;
			_log.LogWarning("module=GameBridge; state=waiting; reason=DialogueManager_not_found");
		}
	}

	private void RemoveTtsMessageNoLock(Guid messageId)
	{
		int count = _ttsQueue.Count;
		for (int i = 0; i < count; i++)
		{
			OrdinaryChatOutput ordinaryChatOutput = _ttsQueue.Dequeue();
			if (ordinaryChatOutput.MessageId != messageId)
			{
				_ttsQueue.Enqueue(ordinaryChatOutput);
			}
		}
	}

	private void InvalidateManager()
	{
		_dialogueManager = null;
		_busyValue = null;
		_dialogueActiveValue = null;
		_currentNodeValue = null;
		_currentNodeTextValue = null;
		_forceSay = null;
		_resetAutoDismiss = null;
		_forceEndDialogue = null;
		_isBubbleTyping = null;
		_replyHoldActive = false;
		_nextResolveAt = 0f;
	}

	private static string EscapeRichText(string text)
	{
		return text.Replace('<', '＜').Replace('>', '＞');
	}

	private static string MessageTag(Guid messageId)
	{
		return messageId.ToString("N").Substring(0, 8);
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 0)
		{
			lock (_messageGate)
			{
				_bubbleQueue.Clear();
				_ttsQueue.Clear();
				_thinkingRequested = false;
				_lastDeliveryStatus = "已停止";
			}
			_thinkingShown = false;
			_thinkingShownAt = 0f;
			_nextThinkingAt = 0f;
			_replyHoldActive = false;
			InvalidateManager();
		}
	}
}
