using Just_Lilith.Core.Llm;

namespace Just_Lilith.Core.Agent;

public sealed record AgentProviderInput(string BaseUrl, string ApiKey, string Model, string ReasoningEffort, LlmApiFormat Protocol);
