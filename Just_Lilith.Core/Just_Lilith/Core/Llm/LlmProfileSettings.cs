using System.Text.Json.Serialization;

namespace Just_Lilith.Core.Llm;

public sealed record LlmProfileSettings
{
	[JsonPropertyName("profile_id")]
	public string ProfileId { get; init; } = "";

	[JsonPropertyName("display_name")]
	public string DisplayName { get; init; } = "";

	[JsonPropertyName("base_url")]
	public string BaseUrl { get; init; } = "";

	[JsonPropertyName("protected_api_key")]
	public string ProtectedApiKey { get; init; } = "";

	[JsonPropertyName("model_id")]
	public string ModelId { get; init; } = "";

	[JsonPropertyName("api_format")]
	public LlmApiFormat ApiFormat { get; init; }

	[JsonPropertyName("reasoning_mode")]
	public ReasoningMode ReasoningMode { get; init; }

	[JsonPropertyName("custom_reasoning_effort")]
	public string CustomReasoningEffort { get; init; } = "low";

	[JsonIgnore]
	public bool HasApiKey => !string.IsNullOrEmpty(ProtectedApiKey);

	[JsonIgnore]
	public bool IsConfigured
	{
		get
		{
			if (!string.IsNullOrEmpty(BaseUrl) && HasApiKey)
			{
				return !string.IsNullOrEmpty(ModelId);
			}
			return false;
		}
	}

	public static LlmProfileSettings Create(int slot)
	{
		return new LlmProfileSettings
		{
			ProfileId = "profile-" + slot,
			DisplayName = "配置 " + slot
		};
	}
}
