using System.IO;
using System.Reflection;

namespace Just_Lilith.Unity.Speech;

internal static class TtsRuntimeLocation
{
	internal static string Resolve()
	{
		Assembly assembly = typeof(TtsRuntimeLocation).Assembly;
		return Path.Combine(Path.GetDirectoryName(assembly.Location), "tts");
	}
}
