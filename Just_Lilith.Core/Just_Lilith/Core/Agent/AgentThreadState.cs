namespace Just_Lilith.Core.Agent;

public sealed record AgentThreadState
{
	public int SchemaVersion { get; init; } = 1;

	public string Owner { get; init; } = "";

	public string Transport { get; init; } = "app-server-v3";

	public string ThreadId { get; init; } = "";

	public string LegacyThreadId { get; init; } = "";
}
