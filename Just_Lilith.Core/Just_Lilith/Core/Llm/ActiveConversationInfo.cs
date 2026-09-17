using System;

namespace Just_Lilith.Core.Llm;

public sealed record ActiveConversationInfo(string SessionId, int? TurnCount, DateTimeOffset? LastActivityAtUtc, string? FirstUserText, bool IsActive, string? ErrorCode)
{
	public string DisplayName { get; init; } = "";
}
