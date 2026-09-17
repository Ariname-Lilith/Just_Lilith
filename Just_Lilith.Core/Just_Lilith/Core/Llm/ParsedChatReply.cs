using Just_Lilith.Core.Speech;

namespace Just_Lilith.Core.Llm;

public sealed record ParsedChatReply(string Text, SpeechDirective Speech, bool UsedPlainTextCompatibility, bool MetadataNormalized)
{
	public string? SpeechText { get; init; }

	public string? SpeechDiagnostic { get; init; }

	public bool SpeechReady
	{
		get
		{
			if (SpeechDiagnostic == null)
			{
				return SpeechTextRules.IsValid(SpeechText);
			}
			return false;
		}
	}
}
