using System;
using System.Linq;
using System.Net;

namespace Just_Lilith.Core.Speech;

public static class SpeechEndpoint
{
	public static string Normalize(string? value)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 || value.Any(char.IsControl) || value.Contains('\\') || !Uri.TryCreate(value, UriKind.Absolute, out Uri result) || result.Scheme != Uri.UriSchemeHttp || result.UserInfo.Length != 0 || result.Query.Length != 0 || result.Fragment.Length != 0 || result.AbsolutePath != "/" || result.Port < 1 || result.Host.Contains('%'))
		{
			throw new SpeechException("service_url", "TTS 地址须为本机 HTTP 地址，例如 http://127.0.0.1:17880。");
		}
		string host;
		if (string.Equals(result.Host, "localhost", StringComparison.OrdinalIgnoreCase))
		{
			host = "127.0.0.1";
		}
		else
		{
			if (!IPAddress.TryParse(result.Host.Trim('[', ']'), out IPAddress address) || !IPAddress.IsLoopback(address))
			{
				throw new SpeechException("service_url", "TTS 服务仅连接本机回环地址。");
			}
			host = address.ToString();
		}
		return new UriBuilder(Uri.UriSchemeHttp, host, result.Port).Uri.AbsoluteUri.TrimEnd('/');
	}
}
