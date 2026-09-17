using System;
using System.Collections.Generic;
using System.Linq;

namespace Just_Lilith.Core.Speech;

public static class SpeechReferenceStyles
{
	public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new string[7] { "neutral", "excited", "sobbing", "hesitant", "sleepy", "tsundere", "ominous" });

	public static string? Canonicalize(string? value)
	{
		return value switch
		{
			"sad" => "sobbing", 
			"acting_tsundere" => "tsundere", 
			"acting_ominous" => "ominous", 
			_ => value, 
		};
	}

	public static bool IsValid(string? value)
	{
		if (value != null)
		{
			return All.Contains<string>(value, StringComparer.Ordinal);
		}
		return false;
	}
}
