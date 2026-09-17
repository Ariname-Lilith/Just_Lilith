using System;
using System.Collections.Generic;

namespace Just_Lilith.Unity.Ui;

public sealed class LlmProfileViewState
{
	public string ProfileId { get; init; } = "";

	public string DisplayName { get; init; } = "";

	public bool IsTop { get; init; }

	public bool HasSavedApiKey { get; init; }

	public string BaseUrl { get; init; } = "";

	public string Protocol { get; init; } = "chat_completions";

	public string SelectedModel { get; init; } = "";

	public string ReasoningMode { get; init; } = "auto";

	public string ReasoningEffort { get; init; } = "low";

	public IReadOnlyList<string> AllowedReasoningEfforts { get; init; } = Array.Empty<string>();

	public bool KnownReasoningModel { get; init; }

	public IReadOnlyList<string> Models { get; init; } = Array.Empty<string>();

	public string Status { get; init; } = "";

	public string EffectiveReasoning { get; init; } = "";

	public bool Dirty { get; init; }
}
