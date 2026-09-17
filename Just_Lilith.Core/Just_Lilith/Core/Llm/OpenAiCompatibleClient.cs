using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Just_Lilith.Core.Llm;

public sealed class OpenAiCompatibleClient : IDisposable
{
	private const int ModelBytesLimit = 2097152;

	private const int ReplyBytesLimit = 8388608;

	private const int ReplyCharacterLimit = 65536;

	private const int ModelCountLimit = 10000;

	private const int ConversationMessageLimit = 64;

	private const int ConversationCharacterLimit = 131072;

	private const int SystemPromptCharacterLimit = 32768;

	private readonly HttpClient http;

	private readonly ISecretProtector protector;

	private int disposed;

	public OpenAiCompatibleClient(ISecretProtector? protector = null, HttpMessageHandler? handler = null)
	{
		this.protector = protector ?? new WindowsSecretProtector();
		http = new HttpClient(handler ?? new HttpClientHandler
		{
			AllowAutoRedirect = false,
			UseCookies = false
		}, disposeHandler: true)
		{
			Timeout = Timeout.InfiniteTimeSpan
		};
	}

	public async Task<IReadOnlyList<string>> ListModelsAsync(LlmProfileSettings settings, CancellationToken cancellationToken = default(CancellationToken))
	{
		JsonDocument jsonDocument = await SendAsync(settings, "models", null, 2097152, TimeSpan.FromSeconds(20.0), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		using (jsonDocument)
		{
			JsonElement rootElement = jsonDocument.RootElement;
			CheckApiError(rootElement);
			if (rootElement.ValueKind != JsonValueKind.Object || !rootElement.TryGetProperty("data", out var value) || value.ValueKind != JsonValueKind.Array)
			{
				throw new LlmException("models_format", "服务端模型列表格式不兼容；应返回 data 数组及模型 id。");
			}
			if (value.GetArrayLength() > 10000)
			{
				throw new LlmException("response_limit", "服务端返回的模型数量超过限制。");
			}
			HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
			List<string> list = new List<string>();
			foreach (JsonElement item in value.EnumerateArray())
			{
				if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var value2) && value2.ValueKind == JsonValueKind.String)
				{
					string text = value2.GetString()?.Trim();
					if (!string.IsNullOrEmpty(text) && text.Length <= 256 && !text.Any(char.IsControl) && hashSet.Add(text))
					{
						list.Add(text);
					}
				}
			}
			if (list.Count == 0)
			{
				throw new LlmException("models_empty", "服务端未返回有效模型。请确认 API 地址、账号权限及服务商是否支持模型列表接口。");
			}
			return Array.AsReadOnly(list.ToArray());
		}
	}

	public async Task<string> CompleteAsync(LlmProfileSettings settings, string input, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(settings.ModelId) || settings.ModelId.Length > 256 || settings.ModelId.Any(char.IsControl))
		{
			throw new LlmException("model_required", "请先选择并保存一个模型。");
		}
		if (string.IsNullOrWhiteSpace(input) || input.Length > 65536)
		{
			throw new LlmException("input_length", "请输入聊天内容，单轮输入最多 65536 个字符。");
		}
		if (!Enum.IsDefined(typeof(LlmApiFormat), settings.ApiFormat))
		{
			throw new LlmException("api_format", "请选择有效的 API 协议。");
		}
		ReasoningDecision reasoningDecision = ReasoningPolicy.Evaluate(settings.ModelId, settings.ReasoningMode, settings.CustomReasoningEffort);
		Dictionary<string, object> dictionary = new Dictionary<string, object>
		{
			["model"] = settings.ModelId,
			["stream"] = false
		};
		bool flag = settings.ApiFormat == LlmApiFormat.Responses;
		if (flag)
		{
			dictionary["input"] = input;
			dictionary["store"] = false;
			if (reasoningDecision.Effort != null)
			{
				dictionary["reasoning"] = new Dictionary<string, string> { ["effort"] = reasoningDecision.Effort };
			}
		}
		else
		{
			dictionary["messages"] = new Dictionary<string, string>[1]
			{
				new Dictionary<string, string>
				{
					["role"] = "user",
					["content"] = input
				}
			};
			if (reasoningDecision.Effort != null)
			{
				dictionary["reasoning_effort"] = reasoningDecision.Effort;
			}
		}
		return await SendCompletionAsync(settings, flag, dictionary, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<string> CompleteAsync(LlmProfileSettings settings, IReadOnlyList<ChatMessage> messages, string? systemPrompt = null, CancellationToken cancellationToken = default(CancellationToken), bool requireJsonObject = false, string? referenceContext = null, string? currentReferenceContext = null)
	{
		if (string.IsNullOrWhiteSpace(settings.ModelId) || settings.ModelId.Length > 256 || settings.ModelId.Any(char.IsControl))
		{
			throw new LlmException("model_required", "请先选择并保存一个模型。");
		}
		if (!Enum.IsDefined(typeof(LlmApiFormat), settings.ApiFormat))
		{
			throw new LlmException("api_format", "请选择有效的 API 协议。");
		}
		if (messages == null || messages.Count == 0 || messages.Count > 64)
		{
			throw new LlmException("conversation_format", $"对话须包含 1 至 {64} 条按顺序排列的消息。");
		}
		if (systemPrompt != null && systemPrompt.Length > 32768)
		{
			throw new LlmException("system_prompt_length", $"系统提示词最多 {32768} 个字符。");
		}
		List<Dictionary<string, string>> list = new List<Dictionary<string, string>>(messages.Count);
		long num = 0L;
		for (int i = 0; i < messages.Count; i++)
		{
			ChatMessage chatMessage = messages[i];
			if ((object)chatMessage == null || chatMessage.Role != (ChatRole)((i % 2 != 0) ? 1 : 0))
			{
				throw new LlmException("conversation_format", "对话消息须从用户开始，按用户与助手交替排列，并以用户消息结束。");
			}
			if (string.IsNullOrWhiteSpace(chatMessage.Content) || chatMessage.Content.Length > 65536)
			{
				throw new LlmException("input_length", $"每条对话消息须有文字且最多 {65536} 个字符。");
			}
			num += chatMessage.Content.Length;
			if (num > 131072)
			{
				throw new LlmException("context_length", $"对话上下文最多 {131072} 个字符，请裁剪较旧回合。");
			}
			list.Add(new Dictionary<string, string>
			{
				["role"] = ((chatMessage.Role == ChatRole.User) ? "user" : "assistant"),
				["content"] = chatMessage.Content
			});
		}
		if (messages.Count % 2 == 0)
		{
			throw new LlmException("conversation_format", "最新一条消息须为用户输入。");
		}
		if (!string.IsNullOrWhiteSpace(currentReferenceContext))
		{
			string text = currentReferenceContext + "\n\n[本轮用户消息]\n" + list[list.Count - 1]["content"];
			if (text.Length <= 65536)
			{
				if (num + text.Length - list[list.Count - 1]["content"].Length <= 131072)
				{
					list[list.Count - 1]["content"] = text;
					goto IL_03d9;
				}
			}
			throw new LlmException("context_length", "本轮参考上下文过长；相关原文件保持不变。");
		}
		goto IL_03d9;
		IL_03d9:
		if (!string.IsNullOrWhiteSpace(referenceContext))
		{
			list.Insert(0, new Dictionary<string, string>
			{
				["role"] = "user",
				["content"] = referenceContext
			});
		}
		ReasoningDecision reasoningDecision = ReasoningPolicy.Evaluate(settings.ModelId, settings.ReasoningMode, settings.CustomReasoningEffort);
		bool flag = settings.ApiFormat == LlmApiFormat.Responses;
		Dictionary<string, object> dictionary = new Dictionary<string, object>
		{
			["model"] = settings.ModelId,
			["stream"] = false
		};
		if (flag)
		{
			dictionary["input"] = list;
			dictionary["store"] = false;
			if (requireJsonObject)
			{
				dictionary["text"] = new
				{
					format = new
					{
						type = "json_object"
					}
				};
			}
			if (!string.IsNullOrEmpty(systemPrompt))
			{
				dictionary["instructions"] = systemPrompt;
			}
			if (reasoningDecision.Effort != null)
			{
				dictionary["reasoning"] = new Dictionary<string, string> { ["effort"] = reasoningDecision.Effort };
			}
		}
		else
		{
			if (!string.IsNullOrEmpty(systemPrompt))
			{
				list.Insert(0, new Dictionary<string, string>
				{
					["role"] = "system",
					["content"] = systemPrompt
				});
			}
			dictionary["messages"] = list;
			if (requireJsonObject)
			{
				dictionary["response_format"] = new
				{
					type = "json_object"
				};
			}
			if (reasoningDecision.Effort != null)
			{
				dictionary["reasoning_effort"] = reasoningDecision.Effort;
			}
		}
		return await SendCompletionAsync(settings, flag, dictionary, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task<string> SendCompletionAsync(LlmProfileSettings settings, bool isResponses, Dictionary<string, object> body, CancellationToken cancellationToken)
	{
		using JsonDocument jsonDocument = await SendAsync(settings, isResponses ? "responses" : "chat/completions", body, 8388608, TimeSpan.FromSeconds(120.0), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		CheckApiError(jsonDocument.RootElement);
		string text = NormalizeVisibleText(isResponses ? ParseResponses(jsonDocument.RootElement) : ParseChat(jsonDocument.RootElement)).Trim();
		if (string.IsNullOrWhiteSpace(text))
		{
			throw new LlmException("empty_reply", "服务端未返回可见回复文字（只有推理字段也不视为回复）。请检查模型与协议兼容性。");
		}
		if (text.Length > 65536)
		{
			throw new LlmException("response_limit", $"可见回复超过 {65536} 个字符，请缩短服务端输出上限。");
		}
		return text;
	}

	private async Task<JsonDocument> SendAsync(LlmProfileSettings settings, string relativePath, object? body, int byteLimit, TimeSpan timeout, CancellationToken cancellationToken)
	{
		if (Volatile.Read(ref disposed) != 0)
		{
			throw new ObjectDisposedException("OpenAiCompatibleClient");
		}
		cancellationToken.ThrowIfCancellationRequested();
		string text = LlmEndpoint.Normalize(settings.BaseUrl);
		if (!settings.HasApiKey)
		{
			throw new LlmException("key_required", "请先输入并保存 API Key。");
		}
		string text2 = protector.Unprotect(settings.ProtectedApiKey);
		if (string.IsNullOrWhiteSpace(text2) || text2.Length > 8192 || text2.Any(char.IsControl))
		{
			throw new LlmException("invalid_key", "已保存的 API Key 格式不正确，请重新输入并保存。");
		}
		using HttpRequestMessage request = new HttpRequestMessage((body == null) ? HttpMethod.Get : HttpMethod.Post, text + "/" + relativePath);
		try
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", text2);
		}
		catch (FormatException)
		{
			throw new LlmException("invalid_key", "已保存的 API Key 无法作为 Bearer 凭据发送，请重新输入。");
		}
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		if (body != null)
		{
			request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
		}
		using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(timeout);
		try
		{
			using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(continueOnCapturedContext: false);
			int statusCode = (int)response.StatusCode;
			if (statusCode >= 300 && statusCode < 400)
			{
				throw new LlmException("redirect_blocked", "API 地址返回了重定向；为保护密钥未跟随跳转，请填写最终 API 地址。", statusCode);
			}
			if (!response.IsSuccessStatusCode)
			{
				throw HttpError(statusCode);
			}
			long? contentLength = response.Content.Headers.ContentLength;
			if (contentLength.HasValue)
			{
				long valueOrDefault = contentLength.GetValueOrDefault();
				if (valueOrDefault > byteLimit)
				{
					throw new LlmException("response_limit", "服务端响应超过大小限制。");
				}
			}
			using Stream stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(continueOnCapturedContext: false);
			using MemoryStream buffer = new MemoryStream();
			byte[] chunk = new byte[16384];
			int num;
			while ((num = await stream.ReadAsync(chunk.AsMemory(), deadline.Token).ConfigureAwait(continueOnCapturedContext: false)) != 0)
			{
				if (buffer.Length + num > byteLimit)
				{
					throw new LlmException("response_limit", "服务端响应超过大小限制。");
				}
				buffer.Write(chunk, 0, num);
			}
			deadline.Token.ThrowIfCancellationRequested();
			return JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions
			{
				MaxDepth = 64
			});
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new LlmException("timeout", "请求超时，请稍后重试或检查服务端状态。");
		}
		catch (HttpRequestException)
		{
			throw new LlmException("network_error", "连接失败，请检查网络、API 地址和证书；详细响应与密钥未写入日志。");
		}
		catch (IOException)
		{
			throw new LlmException("network_error", "读取服务端响应失败，请检查网络后重试。");
		}
		catch (JsonException)
		{
			throw new LlmException("invalid_response", "服务端返回的内容不是有效 JSON，请确认 API 地址与协议。");
		}
	}

	private static LlmException HttpError(int status)
	{
		string text;
		switch (status)
		{
		case 401:
		case 403:
			text = "authentication";
			break;
		case 404:
			text = "endpoint_not_found";
			break;
		case 429:
			text = "rate_limit";
			break;
		default:
			text = "http_error";
			break;
		}
		string code = text;
		string message;
		switch (status)
		{
		case 401:
		case 403:
			message = "API 认证失败或权限不足，请检查 API Key 和账号权限。HTTP " + status;
			break;
		case 404:
			message = "API 接口不存在，请检查基础地址和协议。HTTP 404";
			break;
		case 429:
			message = "服务端限制请求或额度不足，请稍后重试并检查额度。HTTP 429";
			break;
		default:
			message = "服务端请求失败。HTTP " + status;
			break;
		}
		return new LlmException(code, message, status);
	}

	private static void CheckApiError(JsonElement root)
	{
		if (root.ValueKind != JsonValueKind.Object)
		{
			throw new LlmException("invalid_response", "服务端响应结构不兼容。");
		}
		if (root.TryGetProperty("error", out var value) && value.ValueKind != JsonValueKind.Null)
		{
			throw new LlmException("api_error", "服务端报告 API 错误，请检查模型、参数和账号权限。错误原文未显示。");
		}
	}

	private static string ParseChat(JsonElement root)
	{
		if (!root.TryGetProperty("choices", out var value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
		{
			return "";
		}
		JsonElement jsonElement = value[0];
		if (jsonElement.ValueKind != JsonValueKind.Object || !jsonElement.TryGetProperty("message", out var value2) || value2.ValueKind != JsonValueKind.Object)
		{
			return "";
		}
		string text = (value2.TryGetProperty("content", out var value3) ? VisibleContent(value3, responses: false) : "");
		if (string.IsNullOrWhiteSpace(text) && value2.TryGetProperty("refusal", out var value4) && value4.ValueKind == JsonValueKind.String)
		{
			return value4.GetString() ?? "";
		}
		return text;
	}

	private static string ParseResponses(JsonElement root)
	{
		bool flag = root.TryGetProperty("status", out var value) && value.ValueKind == JsonValueKind.String;
		if (flag)
		{
			bool flag2;
			switch (value.GetString())
			{
			case "failed":
			case "cancelled":
			case "incomplete":
			case "queued":
			case "in_progress":
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			flag = flag2;
		}
		if (flag)
		{
			throw new LlmException("reply_incomplete", "服务端回复尚未完成或已中断；本轮未自动重试。");
		}
		List<string> list = new List<string>();
		if (root.TryGetProperty("output", out var value2) && value2.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in value2.EnumerateArray())
			{
				if (item.ValueKind == JsonValueKind.Object && HasString(item, "type", "message") && (!item.TryGetProperty("role", out var value3) || (value3.ValueKind == JsonValueKind.String && !(value3.GetString() != "assistant"))) && item.TryGetProperty("content", out var value4))
				{
					string text = VisibleContent(value4, responses: true);
					if (!string.IsNullOrWhiteSpace(text))
					{
						list.Add(text);
					}
				}
			}
		}
		if (list.Count > 0)
		{
			return string.Join("\n", list);
		}
		if (root.TryGetProperty("output_text", out var value5) && value5.ValueKind == JsonValueKind.String)
		{
			return value5.GetString() ?? "";
		}
		return "";
	}

	private static string VisibleContent(JsonElement content, bool responses)
	{
		if (content.ValueKind == JsonValueKind.String)
		{
			object obj;
			if (!responses)
			{
				obj = content.GetString();
				if (obj == null)
				{
					return "";
				}
			}
			else
			{
				obj = "";
			}
			return (string)obj;
		}
		if (content.ValueKind != JsonValueKind.Array)
		{
			return "";
		}
		StringBuilder stringBuilder = new StringBuilder();
		foreach (JsonElement item in content.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.Object)
			{
				continue;
			}
			JsonElement value2;
			if (HasString(item, "type", "output_text") || (!responses && HasString(item, "type", "text")))
			{
				if (item.TryGetProperty("text", out var value) && value.ValueKind == JsonValueKind.String)
				{
					stringBuilder.Append(value.GetString());
				}
			}
			else if (HasString(item, "type", "refusal") && item.TryGetProperty("refusal", out value2) && value2.ValueKind == JsonValueKind.String)
			{
				stringBuilder.Append(value2.GetString());
			}
		}
		return stringBuilder.ToString();
	}

	private static bool HasString(JsonElement value, string name, string expected)
	{
		if (value.TryGetProperty(name, out var value2) && value2.ValueKind == JsonValueKind.String)
		{
			return value2.GetString() == expected;
		}
		return false;
	}

	private static string NormalizeVisibleText(string value)
	{
		if (!value.Any(delegate(char ch)
		{
			bool flag3 = char.IsControl(ch);
			if (flag3)
			{
				bool flag4;
				switch (ch)
				{
				case '\t':
				case '\n':
				case '\r':
					flag4 = true;
					break;
				default:
					flag4 = false;
					break;
				}
				flag3 = !flag4;
			}
			return flag3;
		}))
		{
			return value;
		}
		StringBuilder stringBuilder = new StringBuilder(value.Length);
		foreach (char c in value)
		{
			bool flag = !char.IsControl(c);
			if (!flag)
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
				flag = flag2;
			}
			if (flag)
			{
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.ToString();
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref disposed, 1) == 0)
		{
			http.Dispose();
		}
	}
}
