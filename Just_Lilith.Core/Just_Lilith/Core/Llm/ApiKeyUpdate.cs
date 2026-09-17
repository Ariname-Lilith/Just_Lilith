namespace Just_Lilith.Core.Llm;

public sealed record ApiKeyUpdate(ApiKeyUpdateMode Mode, string Value = "")
{
	public static ApiKeyUpdate Keep { get; } = new ApiKeyUpdate(ApiKeyUpdateMode.Keep);

	public static ApiKeyUpdate Clear { get; } = new ApiKeyUpdate(ApiKeyUpdateMode.Clear);

	public static ApiKeyUpdate ReplaceWith(string value)
	{
		return new ApiKeyUpdate(ApiKeyUpdateMode.Replace, value);
	}
}
