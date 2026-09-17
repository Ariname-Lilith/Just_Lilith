using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Just_Lilith.Core.Speech;

public sealed class LocalTtsClient : IDisposable
{
	private readonly HttpClient http;

	private readonly string serviceUrl;

	private readonly TimeSpan timeout;

	private int disposed;

	public LocalTtsClient(string serviceUrl = "http://127.0.0.1:17880", HttpMessageHandler? handler = null, TimeSpan? timeout = null)
	{
		this.serviceUrl = SpeechEndpoint.Normalize(serviceUrl);
		this.timeout = timeout ?? TimeSpan.FromSeconds(180.0);
		if (this.timeout <= TimeSpan.Zero || this.timeout > TimeSpan.FromMinutes(10.0))
		{
			throw new SpeechException("timeout_value", "TTS 超时值须在 0 至 600 秒之间。");
		}
		http = new HttpClient(handler ?? new HttpClientHandler
		{
			AllowAutoRedirect = false,
			UseCookies = false,
			UseProxy = false,
			UseDefaultCredentials = false,
			Credentials = null,
			AutomaticDecompression = DecompressionMethods.None
		}, disposeHandler: true)
		{
			Timeout = Timeout.InfiniteTimeSpan
		};
	}

	public async Task<PcmWaveData> SynthesizeAsync(SpeechSynthesisRequest request, CancellationToken cancellationToken = default(CancellationToken))
	{
		SpeechDirective speechDirective = request?.Directive;
		if ((object)speechDirective != null)
		{
			string text = SpeechReferenceStyles.Canonicalize(speechDirective.ReferenceStyle);
			if (text != speechDirective.ReferenceStyle)
			{
				request = request with
				{
					Directive = speechDirective with
					{
						ReferenceStyle = text
					}
				};
			}
		}
		Validate(request);
		byte[] content = JsonSerializer.SerializeToUtf8Bytes(new
		{
			request_id = request.RequestId.ToString("D"),
			text = request.Text,
			language = SpeechLanguages.ToCode(request.Language),
			reference_style = request.Directive.ReferenceStyle,
			reaction_id = request.Directive.ReactionId
		});
		using HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, serviceUrl + "/synthesize")
		{
			Content = new ByteArrayContent(content)
		};
		message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
		{
			CharSet = "utf-8"
		};
		message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/wav"));
		byte[] obj = await SendAsync(message, 33554432, health: false, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (cancellationToken.IsCancellationRequested)
		{
			throw new SpeechException("cancelled", "本次语音请求已取消。");
		}
		return PcmWaveReader.Read(obj);
	}

	public async Task<string> CheckHealthAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		using HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Get, serviceUrl + "/health");
		message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		byte[] array = await SendAsync(message, 8192, health: true, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(array, new JsonDocumentOptions
			{
				MaxDepth = 8
			});
			JsonElement rootElement = jsonDocument.RootElement;
			JsonElement value = default(JsonElement);
			bool flag = rootElement.ValueKind != JsonValueKind.Object || !rootElement.TryGetProperty("status", out value) || value.ValueKind != JsonValueKind.String;
			if (!flag)
			{
				bool flag2;
				switch (value.GetString())
				{
				case "ready":
				case "loading":
				case "error":
					flag2 = true;
					break;
				default:
					flag2 = false;
					break;
				}
				flag = !flag2;
			}
			JsonElement value3 = default(JsonElement);
			bool flag3 = flag || !rootElement.TryGetProperty("service", out var value2) || value2.ValueKind != JsonValueKind.String || value2.GetString() != "Just_Lilith.TTS" || !rootElement.TryGetProperty("model_loaded", out value3);
			if (!flag3)
			{
				JsonValueKind valueKind = value3.ValueKind;
				bool flag2 = valueKind - 5 <= JsonValueKind.Object;
				flag3 = !flag2;
			}
			if (flag3 || (value.GetString() == "ready" && !value3.GetBoolean()))
			{
				throw new SpeechException("health_format", "本机端口返回的 TTS 健康状态不正确。");
			}
			return value.GetString();
		}
		catch (JsonException)
		{
			throw new SpeechException("health_format", "本机端口返回的 TTS 健康状态 JSON 不正确。");
		}
	}

	private async Task<byte[]> SendAsync(HttpRequestMessage request, int maximumBytes, bool health, CancellationToken cancellationToken)
	{
		if (Volatile.Read(ref disposed) != 0)
		{
			throw new ObjectDisposedException("LocalTtsClient");
		}
		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		linked.CancelAfter((health && timeout > TimeSpan.FromSeconds(10.0)) ? TimeSpan.FromSeconds(10.0) : timeout);
		try
		{
			using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(continueOnCapturedContext: false);
			int statusCode = (int)response.StatusCode;
			if (statusCode >= 300 && statusCode <= 399)
			{
				throw new SpeechException("redirect", "本机 TTS 返回了重定向；本次请求已终止。");
			}
			if (!response.IsSuccessStatusCode)
			{
				throw new SpeechException("http_status", $"本机 TTS 返回 HTTP {(int)response.StatusCode}；本次请求未重试。");
			}
			string text = response.Content.Headers.ContentType?.MediaType;
			bool flag;
			if (health)
			{
				flag = text != "application/json";
			}
			else
			{
				bool flag2;
				switch (text)
				{
				case "audio/wav":
				case "audio/x-wav":
				case "audio/wave":
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
				throw new SpeechException("content_type", "本机 TTS 返回了与请求不一致的数据类型。");
			}
			if (response.Content.Headers.ContentEncoding.Count != 0)
			{
				throw new SpeechException("content_type", "本机 TTS 返回了额外编码的数据。");
			}
			long? contentLength = response.Content.Headers.ContentLength;
			if (contentLength.HasValue)
			{
				long valueOrDefault = contentLength.GetValueOrDefault();
				if (valueOrDefault < 0 || valueOrDefault > maximumBytes)
				{
					throw new SpeechException("response_limit", "本机 TTS 响应超过大小限制。");
				}
			}
			using Stream input = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(continueOnCapturedContext: false);
			using MemoryStream output = new MemoryStream();
			byte[] buffer = new byte[16384];
			while (true)
			{
				int num = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), linked.Token).ConfigureAwait(continueOnCapturedContext: false);
				if (num == 0)
				{
					break;
				}
				if (output.Length + num > maximumBytes)
				{
					throw new SpeechException("response_limit", "本机 TTS 响应超过大小限制。");
				}
				output.Write(buffer, 0, num);
			}
			linked.Token.ThrowIfCancellationRequested();
			return output.ToArray();
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw new SpeechException("cancelled", "本次语音请求已取消。");
		}
		catch (OperationCanceledException)
		{
			throw new SpeechException("timeout", "本机 TTS 请求超时；本次请求未重试。");
		}
		catch (Exception ex3) when (((ex3 is HttpRequestException || ex3 is IOException) ? 1 : 0) != 0)
		{
			throw new SpeechException("connection", "连接本机 TTS 失败，请确认语音服务已启动。");
		}
	}

	private static void Validate([NotNull] SpeechSynthesisRequest? request)
	{
		if ((object)request == null || request.RequestId == Guid.Empty)
		{
			throw new SpeechException("request_id", "TTS 请求标识为空。");
		}
		SpeechLanguages.ToCode(request.Language);
		if (!SpeechTextRules.IsValid(request.Text))
		{
			throw new SpeechException("text_length", "TTS 文本须为 1 至 500 个字符，且不含换行、制表以外的控制字符。");
		}
		if ((object)request.Directive == null || !SpeechReferenceStyles.IsValid(request.Directive.ReferenceStyle))
		{
			throw new SpeechException("reference_style", "TTS 参考风格不在已配置的七种类型内。");
		}
		if (!SpeechReactionIds.IsValid(request.Directive.ReactionId))
		{
			throw new SpeechException("reaction_id", "TTS 反应音标识不在已配置的列表内。");
		}
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref disposed, 1) == 0)
		{
			http.Dispose();
		}
	}
}
