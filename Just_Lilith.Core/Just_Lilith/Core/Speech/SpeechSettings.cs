using System.Text.Json.Serialization;
using Just_Lilith.Core.Contracts;

namespace Just_Lilith.Core.Speech;

public sealed record SpeechSettings
{
	[JsonPropertyName("schema_version")]
	public int SchemaVersion { get; init; } = 3;

	[JsonPropertyName("service_enabled")]
	public bool ServiceEnabled { get; init; }

	[JsonPropertyName("enabled")]
	public bool Enabled { get; init; } = true;

	[JsonPropertyName("language")]
	public SpeechLanguage Language { get; init; }

	[JsonPropertyName("reference_mode")]
	public SpeechReferenceMode ReferenceMode { get; init; }

	[JsonPropertyName("forced_reference_style")]
	public string ForcedReferenceStyle { get; init; } = "neutral";

	[JsonPropertyName("reactions_enabled")]
	public bool ReactionsEnabled { get; init; } = true;

	[JsonPropertyName("volume")]
	public float Volume { get; init; } = 1f;

	[JsonPropertyName("service_url")]
	public string ServiceUrl { get; init; } = "http://127.0.0.1:17880";

	public const int CurrentSchemaVersion = 3;
}
