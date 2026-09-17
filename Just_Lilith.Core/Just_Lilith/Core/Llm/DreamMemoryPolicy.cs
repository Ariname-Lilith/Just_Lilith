using System.Linq;
using System.Text;

namespace Just_Lilith.Core.Llm;

public static class DreamMemoryPolicy
{
	public const int RecentTurnLimit = 30;

	public const int RecentCompressionCount = 20;

	public const int ShortTermLimit = 20;

	public const int ShortTermCompressionCount = 10;

	public const int ShortTermCharacterLimit = 150;

	public const int LongTermCharacterLimit = 500;

	private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

	public static int CountCharacters(string text)
	{
		if (text == null)
		{
			throw SummaryError();
		}
		try
		{
			StrictUtf8.GetByteCount(text);
		}
		catch (EncoderFallbackException)
		{
			throw SummaryError();
		}
		int num = 0;
		foreach (Rune item in text.EnumerateRunes())
		{
			_ = item;
			num++;
		}
		return num;
	}

	public static void ValidateSummary(string text, int maximumCharacters)
	{
		if (maximumCharacters < 1 || string.IsNullOrWhiteSpace(text) || text.Any(delegate(char ch)
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
		}) || CountCharacters(text) > maximumCharacters)
		{
			throw SummaryError();
		}
	}

	private static LlmException SummaryError()
	{
		return new LlmException("memory_summary", "记忆摘要为空、超过字符上限或包含无效字符；原始记录保持不变。");
	}
}
