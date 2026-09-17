using System;
using System.Linq;

namespace Just_Lilith.Unity.Game;

internal static class PetStateIdentityResolver
{
	private static readonly (string MemberName, string TypeName)[] StateMembers = new(string, string)[11]
	{
		("_idleState", "LilithIdleState"),
		("_sitState", "LilithSitState"),
		("_sofaSitState", "LilithSofaSitState"),
		("_interactState", "LilithInteractState"),
		("_dragState", "LilithDragState"),
		("_fallState", "LilithFallState"),
		("_actionState", "LilithActionState"),
		("_walkLeftState", "LilithWalkLeftState"),
		("_walkRightState", "LilithWalkRightState"),
		("_sleepState", "LilithSleepState"),
		("_lieState", "LilithLieState")
	};

	public static bool IsKnownTypeName(string? typeName)
	{
		if (!string.IsNullOrEmpty(typeName))
		{
			return StateMembers.Any(((string MemberName, string TypeName) item) => string.Equals(item.TypeName, typeName, StringComparison.Ordinal));
		}
		return false;
	}

	public static string? ResolveTypeName(IntPtr currentPointer, Func<string, IntPtr?> readMemberPointer)
	{
		ArgumentNullException.ThrowIfNull(readMemberPointer, "readMemberPointer");
		if (currentPointer == IntPtr.Zero)
		{
			return null;
		}
		(string, string)[] stateMembers = StateMembers;
		for (int i = 0; i < stateMembers.Length; i++)
		{
			(string, string) tuple = stateMembers[i];
			try
			{
				IntPtr? intPtr = readMemberPointer(tuple.Item1);
				if (intPtr.HasValue)
				{
					IntPtr valueOrDefault = intPtr.GetValueOrDefault();
					if (valueOrDefault != IntPtr.Zero && valueOrDefault == currentPointer)
					{
						return tuple.Item2;
					}
				}
			}
			catch
			{
			}
		}
		return null;
	}
}
