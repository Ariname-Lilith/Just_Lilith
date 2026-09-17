using System;
using System.Collections.Generic;
using Just_Lilith.Core.Contracts;
using Just_Lilith.Core.Speech;

namespace Just_Lilith.Unity.Ui;

internal static class SpeechRadioSelection
{
	internal static IReadOnlyList<SpeechServiceChoice> ServiceChoices { get; } = Array.AsReadOnly(new SpeechServiceChoice[2]
	{
		new SpeechServiceChoice(Enabled: true, "开启"),
		new SpeechServiceChoice(Enabled: false, "关闭")
	});

	internal static IReadOnlyList<SpeechLanguageChoice> LanguageChoices { get; } = Array.AsReadOnly(new SpeechLanguageChoice[2]
	{
		new SpeechLanguageChoice(SpeechLanguage.Chinese, "汉语"),
		new SpeechLanguageChoice(SpeechLanguage.Japanese, "日语")
	});

	internal static IReadOnlyList<SpeechReferenceChoice> ReferenceChoices { get; } = Array.AsReadOnly(new SpeechReferenceChoice[8]
	{
		new SpeechReferenceChoice(null, "自动"),
		new SpeechReferenceChoice("neutral", "日常"),
		new SpeechReferenceChoice("excited", "兴奋"),
		new SpeechReferenceChoice("sobbing", "哭腔"),
		new SpeechReferenceChoice("hesitant", "困惑"),
		new SpeechReferenceChoice("sleepy", "睡意"),
		new SpeechReferenceChoice("tsundere", "傲娇"),
		new SpeechReferenceChoice("ominous", "狡黠")
	});

	internal static string? SelectedReference(SpeechSettings settings)
	{
		if (settings.ReferenceMode != SpeechReferenceMode.Automatic)
		{
			return SpeechReferenceStyles.Canonicalize(settings.ForcedReferenceStyle);
		}
		return null;
	}

	internal static SpeechServiceRadioState ServicePresentation(TtsServicePhase phase, bool loaded)
	{
		switch (phase)
		{
		case TtsServicePhase.Ready:
			return new SpeechServiceRadioState(true, loaded, loaded, "开启", "关闭");
		case TtsServicePhase.Off:
		case TtsServicePhase.Failed:
			return new SpeechServiceRadioState(false, loaded, loaded, "开启", "关闭");
		case TtsServicePhase.Starting:
			return new SpeechServiceRadioState(null, EnableInteractable: false, loaded, "启动中", "关闭");
		case TtsServicePhase.Stopping:
			return new SpeechServiceRadioState(null, EnableInteractable: false, DisableInteractable: false, "开启", "关闭中");
		default:
			return new SpeechServiceRadioState(null, EnableInteractable: false, DisableInteractable: false, "开启", "关闭");
		}
	}

	internal static bool CanRequestServiceEnabled(TtsServicePhase phase, bool enabled, bool loaded)
	{
		bool flag = loaded;
		if (flag)
		{
			bool flag3;
			if (enabled)
			{
				bool flag2 = ((phase == TtsServicePhase.Off || phase == TtsServicePhase.Failed) ? true : false);
				flag3 = flag2;
			}
			else
			{
				bool flag2 = (uint)(phase - 1) <= 1u;
				flag3 = flag2;
			}
			flag = flag3;
		}
		return flag;
	}

	internal static SpeechSettings WithServiceEnabled(SpeechSettings settings, bool enabled)
	{
		if (settings.ServiceEnabled != enabled)
		{
			return settings with
			{
				ServiceEnabled = enabled
			};
		}
		return settings;
	}

	internal static SpeechSettings WithLanguage(SpeechSettings settings, SpeechLanguage language)
	{
		if (!Enum.IsDefined(typeof(SpeechLanguage), language))
		{
			throw new SpeechException("settings_format", "TTS 语言选项不正确。");
		}
		if (settings.Language != language)
		{
			return settings with
			{
				Language = language
			};
		}
		return settings;
	}

	internal static SpeechSettings WithReference(SpeechSettings settings, string? style)
	{
		if (style == null)
		{
			if (settings.ReferenceMode != SpeechReferenceMode.Automatic)
			{
				return settings with
				{
					ReferenceMode = SpeechReferenceMode.Automatic
				};
			}
			return settings;
		}
		style = SpeechReferenceStyles.Canonicalize(style);
		if (!SpeechReferenceStyles.IsValid(style))
		{
			throw new SpeechException("settings_format", "TTS 参考音频选项不正确。");
		}
		if (settings.ReferenceMode != SpeechReferenceMode.Manual || !(settings.ForcedReferenceStyle == style))
		{
			return settings with
			{
				ReferenceMode = SpeechReferenceMode.Manual,
				ForcedReferenceStyle = style
			};
		}
		return settings;
	}
}
