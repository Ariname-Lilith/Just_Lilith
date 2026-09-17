using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Just_Lilith.Core.Contracts;
using Just_Lilith.Core.Speech;

namespace Just_Lilith.Core.Llm;

public static class StructuredChatReply
{
	private static readonly Lazy<string> Catalog = new Lazy<string>(ReadCatalog);

	private const string JapaneseOutputReminder = "\n\n[本轮日语输出格式：最终核对]\n人物资料中默认中文的要求仅用于外层text，不限制speech.text；不要把人物资料里的旧格式当作当前接口。历史assistant消息仅是已显示的中文记忆，不是本轮回复格式示例，不要照着历史省略speech。\n本轮必须在同一个JSON对象中生成两种文本：text为中文显示，speech.text为同义自然日语朗读。speech对象的text、language、reference_style、reaction_id四个字段必须齐全；language固定ja。不要只生成reference_style和reaction_id，不要将日语留在思考过程，不要额外解释格式。\n完整结构示例（只示范格式，不照抄台词；按本轮用户内容作答）：\n{\"text\":\"蛋糕做好了，要尝一块吗？\",\"speech\":{\"text\":\"ケーキができたよ。ひと切れ食べてみる？\",\"language\":\"ja\",\"reference_style\":\"excited\",\"reaction_id\":null}}";

	public static string BuildSystemPrompt(string? systemPrompt)
	{
		return BuildSystemPrompt(systemPrompt, SpeechLanguage.Chinese);
	}

	public static string BuildSystemPrompt(string? systemPrompt, SpeechLanguage speechLanguage)
	{
		string text = SpeechLanguages.ToCode(speechLanguage);
		string text2 = ((speechLanguage == SpeechLanguage.Japanese) ? "与中文正文同义的自然日语朗读文本" : "与外层text完全相同的中文回复");
		return systemPrompt + "\n\n[Just_Lilith 回复输出契约：这是应用格式要求，不是角色台词]\n只返回一个JSON对象，不要Markdown代码围栏、解释或多份回复。格式：{\"text\":\"给用户看的中文回复\",\"speech\":{\"text\":\"" + text2 + "\",\"language\":\"" + text + "\",\"reference_style\":\"neutral\",\"reaction_id\":null}}。\n外层text只供中文气泡和中文会话历史，speech.text只供朗读；两者保持意思、语气、信息一致，不添加另一份答复。通常保持1至3句、160字以内；两个文本字段分别最多500字。不要把思考过程、字段名、参考台词、动作标签或解释放入文本。\n语音语言由UI决定，本轮已固定为" + text + "；speech.language必须逐字为\"" + text + "\"，不要自行切换语言。" + ((speechLanguage == SpeechLanguage.Japanese) ? "speech.text必须是自然日语，不要复制中文后只修改语言标签。\n" : "speech.text必须与外层text逐字相同，不需要另外生成日语。\n") + "JSON不设其他语言字段、模型名或音频路径字段。\n按莉莉丝整段话实际怎样说来选reference_style，不按用户情绪、话题或孤立关键词选。仅事实不确定不代表hesitant，提到晚安不代表sleepy，安慰难过的用户不自动选sobbing。明确且贯穿整段的表演才选tsundere或ominous；一句话混合、只有局部词句带风格或不明确时选neutral。\nreference_style与reaction_id独立：选参考并不意味着必须有反应音。reaction_id默认null，只在确实需要短反应时从白名单选一个；不在text或speech.text机械重复已经选中的短反应。mock_ominous仅在日语模式有录音；当前语言没有对应反应音时由TTS跳过，绝不使用其他语言反应音。\n可用参考、反应音及说明（只能选择键名，不输出说明）：\n" + Catalog.Value + ((speechLanguage == SpeechLanguage.Japanese) ? "\n\n[本轮日语输出格式：最终核对]\n人物资料中默认中文的要求仅用于外层text，不限制speech.text；不要把人物资料里的旧格式当作当前接口。历史assistant消息仅是已显示的中文记忆，不是本轮回复格式示例，不要照着历史省略speech。\n本轮必须在同一个JSON对象中生成两种文本：text为中文显示，speech.text为同义自然日语朗读。speech对象的text、language、reference_style、reaction_id四个字段必须齐全；language固定ja。不要只生成reference_style和reaction_id，不要将日语留在思考过程，不要额外解释格式。\n完整结构示例（只示范格式，不照抄台词；按本轮用户内容作答）：\n{\"text\":\"蛋糕做好了，要尝一块吗？\",\"speech\":{\"text\":\"ケーキができたよ。ひと切れ食べてみる？\",\"language\":\"ja\",\"reference_style\":\"excited\",\"reaction_id\":null}}" : "");
	}

	public static ParsedChatReply Parse(string raw)
	{
		return Parse(raw, SpeechLanguage.Chinese);
	}

	public static ParsedChatReply Parse(string raw, SpeechLanguage expectedLanguage)
	{
		string expectedCode = SpeechLanguages.ToCode(expectedLanguage);
		if (string.IsNullOrWhiteSpace(raw) || raw.Length > 65536)
		{
			throw Invalid();
		}
		string text = raw.Trim();
		int num;
		if (!text.StartsWith("```json", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("```\n", StringComparison.Ordinal))
		{
			num = (text.StartsWith("```\r\n", StringComparison.Ordinal) ? 1 : 0);
			if (num == 0)
			{
				goto IL_0093;
			}
		}
		else
		{
			num = 1;
		}
		int num2 = text.IndexOf('\n');
		if (num2 < 0 || !text.EndsWith("```", StringComparison.Ordinal))
		{
			throw Invalid();
		}
		string text2 = text;
		int num3 = num2 + 1;
		text = text2.Substring(num3, text2.Length - 3 - num3).Trim();
		goto IL_0093;
		IL_0093:
		if (num == 0 && !text.StartsWith("{", StringComparison.Ordinal) && !text.StartsWith("[", StringComparison.Ordinal))
		{
			ValidateText(text);
			return AttachNarration(new ParsedChatReply(text, new SpeechDirective(), UsedPlainTextCompatibility: true, MetadataNormalized: true), default(JsonElement), expectedLanguage, expectedCode);
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(text, new JsonDocumentOptions
			{
				MaxDepth = 8
			});
			JsonElement rootElement = jsonDocument.RootElement;
			if (rootElement.ValueKind != JsonValueKind.Object)
			{
				throw Invalid();
			}
			HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
			foreach (JsonProperty item in rootElement.EnumerateObject())
			{
				bool flag = !hashSet.Add(item.Name);
				if (!flag)
				{
					text2 = item.Name;
					bool flag2 = ((text2 == "text" || text2 == "speech") ? true : false);
					flag = !flag2;
				}
				if (flag)
				{
					throw Invalid();
				}
			}
			if (!rootElement.TryGetProperty("text", out var value) || value.ValueKind != JsonValueKind.String)
			{
				throw Invalid();
			}
			string text3 = value.GetString();
			ValidateText(text3);
			string text4 = "neutral";
			string reactionId = null;
			bool flag3 = false;
			if (rootElement.TryGetProperty("speech", out var value2) && value2.ValueKind == JsonValueKind.Object)
			{
				HashSet<string> hashSet2 = new HashSet<string>(StringComparer.Ordinal);
				foreach (JsonProperty item2 in value2.EnumerateObject())
				{
					bool flag = !hashSet2.Add(item2.Name);
					if (!flag)
					{
						bool flag2;
						switch (item2.Name)
						{
						case "text":
						case "language":
						case "reference_style":
						case "reaction_id":
							flag2 = true;
							break;
						default:
							flag2 = false;
							break;
						}
						flag = !flag2;
					}
					if (flag)
					{
						throw Invalid();
					}
				}
				if (value2.TryGetProperty("reference_style", out var value3) && value3.ValueKind == JsonValueKind.String && SpeechReferenceStyles.IsValid(SpeechReferenceStyles.Canonicalize(value3.GetString())))
				{
					text4 = SpeechReferenceStyles.Canonicalize(value3.GetString());
					flag3 = !string.Equals(text4, value3.GetString(), StringComparison.Ordinal);
				}
				else
				{
					flag3 = true;
				}
				if (value2.TryGetProperty("reaction_id", out var value4))
				{
					if (value4.ValueKind == JsonValueKind.String && SpeechReactionIds.IsValid(value4.GetString()))
					{
						reactionId = value4.GetString();
					}
					else if (value4.ValueKind != JsonValueKind.Null)
					{
						flag3 = true;
					}
				}
			}
			else
			{
				flag3 = true;
			}
			return AttachNarration(new ParsedChatReply(text3, new SpeechDirective(text4, reactionId), UsedPlainTextCompatibility: false, flag3), value2, expectedLanguage, expectedCode);
		}
		catch (JsonException)
		{
			throw Invalid();
		}
	}

	private static ParsedChatReply AttachNarration(ParsedChatReply reply, JsonElement speech, SpeechLanguage expectedLanguage, string expectedCode)
	{
		bool flag = speech.ValueKind == JsonValueKind.Object && speech.TryGetProperty("text", out var value);
		bool flag2 = speech.ValueKind == JsonValueKind.Object && speech.TryGetProperty("language", out value);
		if (!flag && !flag2 && expectedLanguage == SpeechLanguage.Chinese)
		{
			if (!SpeechTextRules.IsValid(reply.Text))
			{
				return NoNarration(reply, "本轮正文超出朗读长度或格式限制；气泡保留，语音已跳过。");
			}
			return reply with
			{
				SpeechText = reply.Text
			};
		}
		if (!flag)
		{
			return NoNarration(reply, "模型未返回 speech.text 朗读文本；气泡保留，语音已跳过。");
		}
		if (!flag2 || speech.GetProperty("language").ValueKind != JsonValueKind.String || !string.Equals(ReadSpeechString(speech.GetProperty("language")), expectedCode, StringComparison.Ordinal))
		{
			return NoNarration(reply, "模型朗读语言与本轮 UI 选择的 " + expectedCode + " 不一致；气泡保留，语音已跳过。");
		}
		string text = ReadSpeechString(speech.GetProperty("text"));
		if (!SpeechTextRules.IsValid(text))
		{
			return NoNarration(reply, "模型朗读文本须为 1 至 500 个有效字符；气泡保留，语音已跳过。");
		}
		if (expectedLanguage == SpeechLanguage.Chinese && !string.Equals(text, reply.Text, StringComparison.Ordinal))
		{
			return NoNarration(reply, "中文朗读文本与气泡正文不一致；气泡保留，语音已跳过。");
		}
		return reply with
		{
			SpeechText = ((text == reply.Text) ? reply.Text : text)
		};
	}

	private static string? ReadSpeechString(JsonElement value)
	{
		if (value.ValueKind != JsonValueKind.String)
		{
			return null;
		}
		try
		{
			return value.GetString();
		}
		catch (InvalidOperationException)
		{
			return null;
		}
	}

	private static ParsedChatReply NoNarration(ParsedChatReply reply, string diagnostic)
	{
		return reply with
		{
			SpeechText = null,
			SpeechDiagnostic = diagnostic,
			MetadataNormalized = true
		};
	}

	private static void ValidateText(string text)
	{
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
			throw Invalid();
		}
	}

	private static LlmException Invalid()
	{
		return new LlmException("reply_contract", "模型回复未满足正文与语音字段格式；本轮尚未保存，请重试。原始元数据未交给气泡或语音。");
	}

	private static string ReadCatalog()
	{
		using Stream stream = typeof(StructuredChatReply).Assembly.GetManifestResourceStream("Just_Lilith.SpeechCatalog") ?? throw new InvalidOperationException("Embedded speech catalog missing.");
		using StreamReader streamReader = new StreamReader(stream, Encoding.UTF8);
		return streamReader.ReadToEnd();
	}
}
