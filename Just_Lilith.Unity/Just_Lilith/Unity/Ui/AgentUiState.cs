using System.Collections.Generic;

namespace Just_Lilith.Unity.Ui;

public sealed record AgentUiState(bool Enabled, bool Busy, bool Ready, string Label, string Detail, string ThreadId, string Model, string ReasoningEffort, IReadOnlyList<string> ModelOptions, IReadOnlyList<string> ReasoningOptions);
