using System;

namespace Just_Lilith.Unity.Ui;

[Flags]
internal enum ChatHotkeyModifiers
{
	None = 0,
	Alt = 1,
	Control = 2,
	Shift = 4,
	Windows = 8
}
