using System;

namespace Just_Lilith.Core.Speech;

public sealed class SpeechException : Exception
{
	public string Code { get; }

	public SpeechException(string code, string message)
		: base(message)
	{
		Code = code;
	}
}
