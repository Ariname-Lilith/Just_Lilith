using System;
using System.Linq;
using Just_Lilith.Core.Contracts;
using Just_Lilith.Core.Speech;

namespace Just_Lilith.Core.Llm;

public sealed record OrdinaryChatOutput
{
	public Guid MessageId { get; }

	public string ProfileId { get; }

	public long SettingsRevision { get; }

	public string ModelId { get; }

	public string Text { get; }

	public string DisplayText => Text;

	public string TtsText { get; }

	public DateTimeOffset CreatedAt { get; }

	public SpeechDirective Speech { get; }

	public SpeechLanguage SpeechLanguage { get; }

	public bool SpeechEnabled { get; }

	public string? SpeechDiagnostic { get; }

	public const int TextCharacterLimit = 65536;

	public OrdinaryChatOutput(Guid messageId, string profileId, long settingsRevision, string modelId, string text, DateTimeOffset createdAt, SpeechDirective? speech = null, SpeechLanguage speechLanguage = SpeechLanguage.Chinese, bool speechEnabled = true, string? speechText = null, string? speechDiagnostic = null)
	{
		if (messageId == Guid.Empty)
		{
			throw new ArgumentException("Message id must not be empty.", "messageId");
		}
		if (string.IsNullOrWhiteSpace(profileId) || profileId.Length > 64 || profileId.Any(char.IsControl))
		{
			throw new ArgumentException("Profile id is invalid.", "profileId");
		}
		if (settingsRevision < 0)
		{
			throw new ArgumentOutOfRangeException("settingsRevision");
		}
		if (string.IsNullOrWhiteSpace(modelId) || modelId.Length > 256 || modelId.Any(char.IsControl))
		{
			throw new ArgumentException("Model id is invalid.", "modelId");
		}
		if (string.IsNullOrWhiteSpace(text) || text.Length > 65536 || text.Any(delegate(char ch)
		{
			bool flag = char.IsControl(ch);
			if (flag)
			{
				bool flag2;
				switch (ch)
				{
				case '\t':
				case '\n':
				case '\r':
					flag2 = true;
					break;
				default:
					flag2 = false;
					break;
				}
				flag = !flag2;
			}
			return flag;
		}))
		{
			throw new ArgumentException("Visible text is invalid.", "text");
		}
		SpeechDirective speechDirective = speech ?? new SpeechDirective();
		string text2 = SpeechReferenceStyles.Canonicalize(speechDirective.ReferenceStyle);
		if (text2 != speechDirective.ReferenceStyle)
		{
			speechDirective = speechDirective with
			{
				ReferenceStyle = text2
			};
		}
		if (!SpeechReferenceStyles.IsValid(speechDirective.ReferenceStyle) || (speechDirective.ReactionId != null && !SpeechReactionIds.IsValid(speechDirective.ReactionId)))
		{
			throw new ArgumentException("Speech directive is invalid.", "speech");
		}
		if (!Enum.IsDefined(typeof(SpeechLanguage), speechLanguage))
		{
			throw new ArgumentOutOfRangeException("speechLanguage");
		}
		Speech = speechDirective;
		SpeechLanguage = speechLanguage;
		string text3 = speechText ?? ((speechLanguage == SpeechLanguage.Chinese) ? text : null);
		SpeechDiagnostic = speechDiagnostic ?? ((!SpeechTextRules.IsValid(text3)) ? "本轮朗读文本缺失或格式不正确；气泡保留，语音已跳过。" : null);
		if (SpeechDiagnostic == null && speechLanguage == SpeechLanguage.Chinese && !string.Equals(text3, text, StringComparison.Ordinal))
		{
			SpeechDiagnostic = "中文朗读文本与气泡正文不一致；气泡保留，语音已跳过。";
		}
		TtsText = ((SpeechDiagnostic == null) ? text3 : "");
		SpeechEnabled = speechEnabled && SpeechDiagnostic == null;
		MessageId = messageId;
		ProfileId = profileId;
		SettingsRevision = settingsRevision;
		ModelId = modelId;
		Text = text;
		CreatedAt = createdAt;
	}
}
