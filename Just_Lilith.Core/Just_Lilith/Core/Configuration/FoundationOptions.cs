using System.Text.Json.Serialization;

namespace Just_Lilith.Core.Configuration;

public sealed record FoundationOptions
{
	[JsonPropertyName("schema_version")]
	public int SchemaVersion { get; init; } = 1;

	[JsonPropertyName("enabled")]
	public bool Enabled { get; init; } = true;

	[JsonPropertyName("verbose_logging")]
	public bool VerboseLogging { get; init; }
}
