using System.Text.Json.Serialization;

namespace Just_Lilith.Core.Agent;

public sealed record AgentOptions
{
	[JsonPropertyName("schema_version")]
	public int SchemaVersion { get; init; } = 1;

	[JsonPropertyName("enabled")]
	public bool Enabled { get; init; }

	[JsonPropertyName("project_root")]
	public string ProjectRoot { get; init; } = "";

	[JsonPropertyName("persona_path")]
	public string PersonaPath { get; init; } = "";

	[JsonPropertyName("model")]
	public string Model { get; init; } = "";

	[JsonPropertyName("reasoning_effort")]
	public string ReasoningEffort { get; init; } = "high";

	[JsonPropertyName("timeout_seconds")]
	public int TimeoutSeconds { get; init; } = 1800;

	[JsonPropertyName("codex_executable")]
	public string CodexExecutable { get; init; } = "";

	[JsonPropertyName("codex_home")]
	public string CodexHome { get; init; } = "";

	[JsonPropertyName("provider_mode")]
	public string ProviderMode { get; init; } = "auto";

	[JsonPropertyName("codex_model_provider")]
	public string CodexModelProvider { get; init; } = "";

	[JsonPropertyName("api_key_environment_variable")]
	public string ApiKeyEnvironmentVariable { get; init; } = "CUSTOM_API_KEY";
}
