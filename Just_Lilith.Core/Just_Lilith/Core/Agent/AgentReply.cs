using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Just_Lilith.Core.Contracts;
using Just_Lilith.Core.Llm;
using Just_Lilith.Core.Speech;

namespace Just_Lilith.Core.Agent;

public static class AgentReply
{
	public const int RawCharacterLimit = 787456;

	public static JsonElement CreateOutputSchema(SpeechLanguage language)
	{
		return JsonSerializer.SerializeToElement(new
		{
			type = "object",
			properties = new
			{
				text = new
				{
					type = "string"
				},
				speech = new
				{
					type = "object",
					properties = new
					{
						text = new
						{
							type = "string"
						},
						language = new
						{
							type = "string",
							@enum = new string[1] { SpeechLanguages.ToCode(language) }
						},
						reference_style = new
						{
							type = "string",
							@enum = SpeechReferenceStyles.All
						},
						reaction_id = new
						{
							type = new string[2] { "string", "null" },
							@enum = SpeechReactionIds.All.Cast<string>().Append(null).ToArray()
						}
					},
					required = new string[4] { "text", "language", "reference_style", "reaction_id" },
					additionalProperties = false
				}
			},
			required = new string[2] { "text", "speech" },
			additionalProperties = false
		});
	}

	public static string BuildPrompt(string userText, SpeechLanguage language)
	{
		string text = SpeechLanguages.ToCode(language);
		return userText + "\n\n[Companion output transport] Your final answer must be exactly one JSON object matching the supplied outputSchema, with no surrounding prose or Markdown fences. All fields are required: {\"text\":\"visible answer\",\"speech\":{\"text\":\"narration\",\"language\":\"" + text + "\",\"reference_style\":\"neutral\",\"reaction_id\":null}}. Put the user's requested answer format (including code, Markdown or JSON) inside the text string, never outside the object. Keep the final answer concise and factual; do not claim an action succeeded without tool evidence. Do not speak tool logs. This format applies only to the final answer, not tool calls or progress updates. The UI fixes speech.language to " + text + "; do not choose another language. " + ((language == SpeechLanguage.Japanese) ? "speech.text must be natural Japanese narration with the same meaning as text, preferably at most 500 characters. " : "speech.text must exactly equal text, including whitespace. Prefer at most 500 characters for spoken replies. ") + "Long text remains visible but narration over 500 characters is not spoken; do not truncate a requested artifact. Use neutral and null by default. Only choose a schema-listed reference_style or reaction_id when appropriate to the actual delivery. Persona or old conversation examples do not change this transport. No extra fields or legacy display_text/speech_ja format.";
	}

	public static ParsedChatReply Parse(string raw, SpeechLanguage language)
	{
		string text = SpeechLanguages.ToCode(language);
		if (string.IsNullOrWhiteSpace(raw) || raw.Length > 787456)
		{
			throw Invalid();
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(raw, new JsonDocumentOptions
			{
				MaxDepth = 8
			});
			JsonElement rootElement = jsonDocument.RootElement;
			RequireFields(rootElement, "text", "speech");
			string text2 = ReadText(rootElement.GetProperty("text"));
			JsonElement property = rootElement.GetProperty("speech");
			RequireFields(property, "text", "language", "reference_style", "reaction_id");
			string text3 = ReadText(property.GetProperty("text"));
			if (ReadString(property.GetProperty("language")) != text)
			{
				throw Invalid();
			}
			string text4 = ReadString(property.GetProperty("reference_style"));
			if (!SpeechReferenceStyles.IsValid(text4))
			{
				throw Invalid();
			}
			JsonElement property2 = property.GetProperty("reaction_id");
			string text5 = ((property2.ValueKind == JsonValueKind.Null) ? null : ReadString(property2));
			if (!SpeechReactionIds.IsValid(text5))
			{
				throw Invalid();
			}
			if (language == SpeechLanguage.Chinese && !string.Equals(text2, text3, StringComparison.Ordinal))
			{
				throw Invalid();
			}
			SpeechDirective speech = new SpeechDirective(text4, text5);
			OrdinaryChatOutput ordinaryChatOutput = new OrdinaryChatOutput(Guid.NewGuid(), "agent", 0L, "codex", text2, DateTimeOffset.UtcNow, speech, language, speechEnabled: true, text3);
			return new ParsedChatReply(text2, speech, UsedPlainTextCompatibility: false, MetadataNormalized: false)
			{
				SpeechText = ((ordinaryChatOutput.SpeechDiagnostic == null) ? text3 : null),
				SpeechDiagnostic = ordinaryChatOutput.SpeechDiagnostic
			};
		}
		catch (JsonException)
		{
			throw Invalid();
		}
		catch (InvalidOperationException)
		{
			throw Invalid();
		}
	}

	private static void RequireFields(JsonElement value, params string[] required)
	{
		if (value.ValueKind != JsonValueKind.Object)
		{
			throw Invalid();
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		foreach (JsonProperty item in value.EnumerateObject())
		{
			if (!required.Contains<string>(item.Name, StringComparer.Ordinal) || !hashSet.Add(item.Name))
			{
				throw Invalid();
			}
		}
		if (hashSet.Count != required.Length)
		{
			throw Invalid();
		}
	}

	private static string ReadString(JsonElement value)
	{
		if (value.ValueKind != JsonValueKind.String)
		{
			throw Invalid();
		}
		return value.GetString();
	}

	private static string ReadText(JsonElement value)
	{
		string text = ReadString(value);
		if (string.IsNullOrWhiteSpace(text) || text.Length > 65536 || text.Any(delegate(char c)
		{
			bool flag = char.IsControl(c);
			if (flag)
			{
				bool flag2;
				switch (c)
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
			throw Invalid();
		}
		for (int num = 0; num < text.Length; num++)
		{
			if (char.IsHighSurrogate(text[num]))
			{
				if (++num >= text.Length || !char.IsLowSurrogate(text[num]))
				{
					throw Invalid();
				}
			}
			else if (char.IsLowSurrogate(text[num]))
			{
				throw Invalid();
			}
		}
		return text;
	}

	private static LlmException Invalid()
	{
		return new LlmException("agent_reply_contract", "Agent 最终回复未满足统一 JSON 格式；未交付气泡或语音。任务操作可能已生效，请打开专属对话核对，不要直接重发原操作。");
	}
}
