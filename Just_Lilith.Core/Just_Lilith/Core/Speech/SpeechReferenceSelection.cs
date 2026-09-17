using System;

namespace Just_Lilith.Core.Speech;

public static class SpeechReferenceSelection
{
	public static SpeechDirective Resolve(SpeechDirective directive, SpeechSettings settings)
	{
		if ((object)settings == null || !Enum.IsDefined(typeof(SpeechReferenceMode), settings.ReferenceMode) || !SpeechReferenceStyles.IsValid(SpeechReferenceStyles.Canonicalize(settings.ForcedReferenceStyle)))
		{
			throw new SpeechException("settings_format", "TTS 参考模式或强制参考类型不正确。");
		}
		if ((object)directive == null)
		{
			throw new SpeechException("reference_style", "TTS 语音指令为空。");
		}
		string text = SpeechReferenceStyles.Canonicalize((settings.ReferenceMode == SpeechReferenceMode.Manual) ? settings.ForcedReferenceStyle : directive.ReferenceStyle);
		return directive with
		{
			ReferenceStyle = (SpeechReferenceStyles.IsValid(text) ? text : "neutral")
		};
	}
}
