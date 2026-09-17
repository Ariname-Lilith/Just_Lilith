using System;

namespace Just_Lilith.Core.Llm;

public sealed record ConversationEvent(ConversationEventType Type, string Content, DateTimeOffset OccurredAtUtc);
