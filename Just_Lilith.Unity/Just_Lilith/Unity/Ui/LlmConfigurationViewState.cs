using System;
using System.Collections.Generic;

namespace Just_Lilith.Unity.Ui;

public sealed class LlmConfigurationViewState
{
	public IReadOnlyList<LlmProfileViewState> Profiles { get; init; } = Array.Empty<LlmProfileViewState>();

	public string GlobalStatus { get; init; } = "";

	public string Reply { get; init; } = "";

	public string ActiveSummary { get; init; } = "顶部配置尚未完成。";
}
