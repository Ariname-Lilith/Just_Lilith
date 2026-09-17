using System;

namespace Just_Lilith.Unity.Ui;

internal static class ChatHotkeyPreferenceMigration
{
	public static ChatHotkeyBinding Load(string currentKey, string legacyKey, Func<string, bool> hasKey, Func<string, int> getInt, Action<string, int> setInt, Action save, Action<Exception>? onWriteError = null)
	{
		if (hasKey(currentKey))
		{
			return ChatHotkeyBinding.DecodeOrDefault(getInt(currentKey));
		}
		if (!hasKey(legacyKey))
		{
			return ChatHotkeyBinding.Default;
		}
		int num = getInt(legacyKey);
		ChatHotkeyBinding result = ChatHotkeyBinding.DecodeOrDefault(num);
		if (result.Encode() != num)
		{
			return ChatHotkeyBinding.Default;
		}
		try
		{
			setInt(currentKey, num);
			save();
		}
		catch (Exception obj)
		{
			try
			{
				onWriteError?.Invoke(obj);
			}
			catch
			{
			}
		}
		return result;
	}
}
