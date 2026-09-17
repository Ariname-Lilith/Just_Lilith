using System.Linq;

namespace Just_Lilith.Core.Speech;

public static class SpeechTextRules
{
	public const int CharacterLimit = 500;

	public static bool IsValid(string? text)
	{
		if (string.IsNullOrWhiteSpace(text) || text.Length > 500 || text.Any(delegate(char c)
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
			return false;
		}
		for (int num = 0; num < text.Length; num++)
		{
			if (char.IsHighSurrogate(text[num]))
			{
				if (++num >= text.Length || !char.IsLowSurrogate(text[num]))
				{
					return false;
				}
			}
			else if (char.IsLowSurrogate(text[num]))
			{
				return false;
			}
		}
		return true;
	}
}
