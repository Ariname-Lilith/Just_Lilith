using System.Linq;
using System.Text.Json.Serialization;

namespace Just_Lilith.Core.Llm;

public sealed record LlmSettings
{
	[JsonPropertyName("schema_version")]
	public int SchemaVersion { get; init; } = 2;

	[JsonPropertyName("revision")]
	public long Revision { get; init; }

	[JsonPropertyName("profiles")]
	public LlmProfileSettings[] Profiles { get; init; } = CreateDefaultProfiles();

	[JsonIgnore]
	public LlmProfileSettings ActiveProfile => Profiles[0];

	public const int CurrentSchemaVersion = 2;

	public const int ProfileCount = 3;

	public static LlmSettings CreateDefault()
	{
		return new LlmSettings();
	}

	public static LlmProfileSettings[] CreateDefaultProfiles()
	{
		return Enumerable.Range(1, 3).Select(LlmProfileSettings.Create).ToArray();
	}
}
