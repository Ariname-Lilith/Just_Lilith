using System.Collections.Generic;

namespace Just_Lilith.Core.Llm;

public sealed record ReasoningDecision(bool KnownModel, string? Effort, IReadOnlyList<string> AllowedEfforts, string Description);
