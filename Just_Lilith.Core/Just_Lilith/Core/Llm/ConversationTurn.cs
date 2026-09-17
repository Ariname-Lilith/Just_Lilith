using System;

namespace Just_Lilith.Core.Llm;

public sealed record ConversationTurn(string User, string Assistant, DateTimeOffset UserOccurredAtUtc, DateTimeOffset AssistantOccurredAtUtc);
