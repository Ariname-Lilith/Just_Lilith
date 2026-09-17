using System;

namespace Just_Lilith.Unity.Game;

internal static class RuntimeValueFallback
{
	public static bool? First(bool? primary, Func<bool?> fallback)
	{
		ArgumentNullException.ThrowIfNull(fallback, "fallback");
		return primary ?? fallback();
	}

	public static string? First(string? primary, Func<string?> fallback)
	{
		ArgumentNullException.ThrowIfNull(fallback, "fallback");
		return primary ?? fallback();
	}
}
