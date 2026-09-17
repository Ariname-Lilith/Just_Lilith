using System.Collections.Generic;

namespace Just_Lilith.AgentNative;

internal sealed record CatalogModel(string Model, bool IsDefault, string DefaultReasoning, IReadOnlyList<string> Reasoning);
