using System.Collections.Generic;

namespace Just_Lilith.Unity.Ui;

internal readonly record struct ChatHotkeyBinding(ChatHotkeyModifiers Modifiers, int VirtualKey)
{
	public static ChatHotkeyBinding Default { get; } = new ChatHotkeyBinding(ChatHotkeyModifiers.None, 118);

	public bool IsValid => IsValidCombination(Modifiers, VirtualKey);

	private const int ModifierMask = 15;

	private const int VirtualKeyMask = 255;

	private const int EncodedMask = 4095;

	private const ChatHotkeyModifiers CommandModifiers = ChatHotkeyModifiers.Alt | ChatHotkeyModifiers.Control | ChatHotkeyModifiers.Windows;

	public const int VirtualKeyF7 = 118;

	public int Encode()
	{
		return ((int)(Modifiers & (ChatHotkeyModifiers.Alt | ChatHotkeyModifiers.Control | ChatHotkeyModifiers.Shift | ChatHotkeyModifiers.Windows)) << 8) | (VirtualKey & 0xFF);
	}

	public static ChatHotkeyBinding DecodeOrDefault(int encoded)
	{
		if ((encoded & -4096) != 0)
		{
			return Default;
		}
		ChatHotkeyModifiers modifiers = (ChatHotkeyModifiers)((encoded >> 8) & 0xF);
		int virtualKey = encoded & 0xFF;
		ChatHotkeyBinding result = new ChatHotkeyBinding(modifiers, virtualKey);
		if (!result.IsValid)
		{
			return Default;
		}
		return result;
	}

	public string ToDisplayString()
	{
		List<string> list = new List<string>(5);
		if ((Modifiers & ChatHotkeyModifiers.Control) != ChatHotkeyModifiers.None)
		{
			list.Add("Ctrl");
		}
		if ((Modifiers & ChatHotkeyModifiers.Alt) != ChatHotkeyModifiers.None)
		{
			list.Add("Alt");
		}
		if ((Modifiers & ChatHotkeyModifiers.Shift) != ChatHotkeyModifiers.None)
		{
			list.Add("Shift");
		}
		if ((Modifiers & ChatHotkeyModifiers.Windows) != ChatHotkeyModifiers.None)
		{
			list.Add("Win");
		}
		list.Add(KeyName(VirtualKey));
		return string.Join(" + ", list);
	}

	public static bool IsBindableVirtualKey(int key)
	{
		if ((key < 8 || key > 254) ? true : false)
		{
			return false;
		}
		bool flag;
		switch (key)
		{
		case 9:
		case 13:
		case 16:
		case 17:
		case 18:
		case 32:
		case 91:
		case 92:
		case 160:
		case 161:
		case 162:
		case 163:
		case 164:
		case 165:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		return !flag;
	}

	public static bool IsValidCombination(ChatHotkeyModifiers modifiers, int key)
	{
		if ((modifiers & ~(ChatHotkeyModifiers.Alt | ChatHotkeyModifiers.Control | ChatHotkeyModifiers.Shift | ChatHotkeyModifiers.Windows)) != ChatHotkeyModifiers.None || !IsBindableVirtualKey(key))
		{
			return false;
		}
		if (RequiresCommandModifier(key))
		{
			return (modifiers & (ChatHotkeyModifiers.Alt | ChatHotkeyModifiers.Control | ChatHotkeyModifiers.Windows)) != 0;
		}
		return true;
	}

	public static bool RequiresCommandModifier(int key)
	{
		switch (key)
		{
		case 48:
		case 49:
		case 50:
		case 51:
		case 52:
		case 53:
		case 54:
		case 55:
		case 56:
		case 57:
		case 65:
		case 66:
		case 67:
		case 68:
		case 69:
		case 70:
		case 71:
		case 72:
		case 73:
		case 74:
		case 75:
		case 76:
		case 77:
		case 78:
		case 79:
		case 80:
		case 81:
		case 82:
		case 83:
		case 84:
		case 85:
		case 86:
		case 87:
		case 88:
		case 89:
		case 90:
		case 96:
		case 97:
		case 98:
		case 99:
		case 100:
		case 101:
		case 102:
		case 103:
		case 104:
		case 105:
		case 106:
		case 107:
		case 109:
		case 110:
		case 111:
		case 186:
		case 187:
		case 188:
		case 189:
		case 190:
		case 191:
		case 192:
		case 219:
		case 220:
		case 221:
		case 222:
		case 223:
		case 226:
			return true;
		default:
			return false;
		}
	}

	internal static string KeyName(int key)
	{
		if (key >= 65 && key <= 90)
		{
			return ((char)key).ToString();
		}
		if (key >= 48 && key <= 57)
		{
			return ((char)key).ToString();
		}
		if (key >= 112 && key <= 135)
		{
			return "F" + (key - 111);
		}
		if (key >= 96 && key <= 105)
		{
			return "Num " + (key - 96);
		}
		return key switch
		{
			8 => "Backspace", 
			9 => "Tab", 
			13 => "Enter", 
			19 => "Pause", 
			20 => "Caps Lock", 
			27 => "Esc", 
			32 => "Space", 
			33 => "Page Up", 
			34 => "Page Down", 
			35 => "End", 
			36 => "Home", 
			37 => "Left", 
			38 => "Up", 
			39 => "Right", 
			40 => "Down", 
			44 => "Print Screen", 
			45 => "Insert", 
			46 => "Delete", 
			106 => "Num *", 
			107 => "Num +", 
			109 => "Num -", 
			110 => "Num .", 
			111 => "Num /", 
			144 => "Num Lock", 
			145 => "Scroll Lock", 
			186 => ";", 
			187 => "=", 
			188 => ",", 
			189 => "-", 
			190 => ".", 
			191 => "/", 
			192 => "`", 
			219 => "[", 
			220 => "\\", 
			221 => "]", 
			222 => "'", 
			_ => "VK " + key.ToString("X2"), 
		};
	}
}
