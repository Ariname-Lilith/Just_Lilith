using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Just_Lilith.Core.Llm;

namespace Just_Lilith.Sync;

public static class BubbleSpeechSync
{
	public const double MinimumDisplayDelaySeconds = 1.25;

	public const double MaximumDisplayDelaySeconds = 4.0;

	public const double ClosingTailSeconds = 3.0;

	private static int Length(string text)
	{
		return text?.Count((char c) => !char.IsWhiteSpace(c)) ?? 0;
	}

	private static bool HasSpeech(OrdinaryChatOutput o)
	{
		if (o.SpeechEnabled && o.SpeechDiagnostic == null)
		{
			return Length(o.TtsText) > 0;
		}
		return false;
	}

	public static BubblePresentationSchedule Plan(OrdinaryChatOutput o)
	{
		BubblePresentationSchedule result = BubblePresentationTiming.Plan(o.DisplayText, o.TtsText, o.SpeechLanguage, o.SpeechEnabled);
		if (!HasSpeech(o))
		{
			return result;
		}
		double delay = Math.Clamp(1.25 + (double)Length(o.TtsText) / 60.0, 1.25, 4.0);
		double hold = Math.Max(result.HoldDuration.TotalSeconds + 3.0, Math.Max((double)Length(o.DisplayText) / 6.5, (double)Length(o.TtsText) / 4.5) + 3.0);
		double typing = BubblePresentationTiming.PlanTypingDuration(o.DisplayText).TotalSeconds;
		return new BubblePresentationSchedule(TimeSpan.FromSeconds(delay), TimeSpan.FromSeconds(typing + hold), TimeSpan.FromSeconds(hold));
	}

	public static bool IsReady(DateTimeOffset enqueuedAt, DateTimeOffset now, OrdinaryChatOutput o)
	{
		return now - enqueuedAt >= Plan(o).Delay;
	}

	public static void EnqueueSpeech(object bridge, OrdinaryChatOutput output)
	{
		if (!HasSpeech(output))
		{
			return;
		}
		BindingFlags bindingAttr = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		object? value = bridge.GetType().GetField("_messageGate", bindingAttr).GetValue(bridge);
		Queue<OrdinaryChatOutput> queue = (Queue<OrdinaryChatOutput>)bridge.GetType().GetField("_ttsQueue", bindingAttr).GetValue(bridge);
		lock (value)
		{
			if (queue.Count >= 16)
			{
				queue.Dequeue();
			}
			queue.Enqueue(output);
		}
	}
}
