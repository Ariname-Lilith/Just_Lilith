using System;
using System.Linq;

namespace Just_Lilith.Core.Llm;

public static class LlmEndpoint
{
	public static string Normalize(string input)
	{
		if (string.IsNullOrWhiteSpace(input) || input.Length > 2048 || input.Any(char.IsControl) || input.Contains('\\') || !Uri.TryCreate(input.Trim(), UriKind.Absolute, out Uri result) || !string.IsNullOrEmpty(result.UserInfo) || !string.IsNullOrEmpty(result.Query) || !string.IsNullOrEmpty(result.Fragment) || (!(result.Scheme == Uri.UriSchemeHttps) && (!(result.Scheme == Uri.UriSchemeHttp) || !result.IsLoopback)))
		{
			throw new LlmException("invalid_url", "请输入 HTTPS API 地址（本机服务也可使用 HTTP），地址不应含账号、查询参数或片段。");
		}
		string text = result.AbsolutePath.TrimEnd('/');
		string[] array = new string[3] { "/chat/completions", "/responses", "/models" };
		foreach (string text2 in array)
		{
			if (text.EndsWith(text2, StringComparison.OrdinalIgnoreCase))
			{
				string text3 = text;
				int length = text2.Length;
				text = text3.Substring(0, text3.Length - length).TrimEnd('/');
				break;
			}
		}
		if (text.Length == 0)
		{
			text = "/v1";
		}
		return result.GetLeftPart(UriPartial.Authority) + text;
	}
}
