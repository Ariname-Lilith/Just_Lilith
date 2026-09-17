using System;

namespace Just_Lilith.Core.Llm;

public sealed class LlmException : Exception
{
	public string Code { get; }

	public int? StatusCode { get; }

	public LlmException(string code, string message, int? statusCode = null)
		: base(message)
	{
		Code = code;
		StatusCode = statusCode;
	}
}
