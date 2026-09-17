using System;
using System.Collections.Generic;
using Just_Lilith.Core.Llm;

namespace Just_Lilith.Unity.Ui;

public sealed class LlmConfigurationCallbacks
{
	public Action<LlmProfileDraft>? SaveProfileRequested { get; init; }

	public Action<string>? RefreshProfileRequested { get; init; }

	public Action? CancelConnectionRequested { get; init; }

	public Action<string, int>? MoveProfileRequested { get; init; }

	public Action<LlmProfileDraft>? DraftChanged { get; init; }

	public Action<string>? SendRequested { get; init; }

	public Action? ChatCancelRequested { get; init; }

	public Action? OpenSystemPromptRequested { get; init; }

	public Action? OpenDreamMemoryRequested { get; init; }

	public Action? OpenWorldBookRequested { get; init; }

	public Func<IReadOnlyList<ActiveConversationInfo>>? ListConversationsRequested { get; init; }

	public Func<string>? StartConversationRequested { get; init; }

	public Func<string, string?, string>? SwitchConversationRequested { get; init; }

	public Func<string, string, string>? RenameConversationRequested { get; init; }

	public Func<string, string?, string>? DeleteConversationRequested { get; init; }

	public Action? AgentToggleRequested { get; init; }

	public Action? AgentSettingsRequested { get; init; }

	public Action? AgentPersonaRequested { get; init; }

	public Action<string>? AgentModelSelectedRequested { get; init; }

	public Action<string>? AgentReasoningSelectedRequested { get; init; }

	public Func<AgentUiState>? AgentStatusRequested { get; init; }

	public Action? Stopped { get; init; }
}
