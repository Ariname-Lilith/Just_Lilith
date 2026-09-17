using Just_Lilith.Core.Contracts;

namespace Just_Lilith.Core.Speech;

public static class SpeechLanguages
{
	public static string ToCode(SpeechLanguage language)
	{
		return language switch
		{
			SpeechLanguage.Chinese => "zh", 
			SpeechLanguage.Japanese => "ja", 
			_ => throw new SpeechException("language_not_supported", "TTS 仅支持中文 zh 与日语 ja。"), 
		};
	}
}
