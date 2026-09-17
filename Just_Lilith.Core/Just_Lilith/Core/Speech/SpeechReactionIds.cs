using System;
using System.Collections.Generic;
using System.Linq;

namespace Just_Lilith.Core.Speech;

public static class SpeechReactionIds
{
	public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new string[9] { "happy", "hesitant", "pain", "questioning", "shy", "sleepy", "surprised", "whimper", "mock_ominous" });

	public static bool IsValid(string? value)
	{
		if (value != null)
		{
			return All.Contains<string>(value, StringComparer.Ordinal);
		}
		return true;
	}
}
