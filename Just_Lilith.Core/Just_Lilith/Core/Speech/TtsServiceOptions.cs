using System;

namespace Just_Lilith.Core.Speech;

public sealed record TtsServiceOptions(string RuntimeDirectory)
{
	public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(180.0);

	public TimeSpan HealthPollInterval { get; init; } = TimeSpan.FromMilliseconds(500.0);

	public TimeSpan HealthRequestTimeout { get; init; } = TimeSpan.FromSeconds(2.0);

	public int LogCapacity { get; init; } = 64;

	internal Func<string, int, string, Action<string>, ITtsManagedProcess>? ProcessFactory { get; init; }
}
