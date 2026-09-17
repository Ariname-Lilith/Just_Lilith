namespace Just_Lilith.Core.Contracts;

public sealed record ReplyFailed(RequestIdentity Request, WorkStage Stage, string Code, string UserMessage) : ReplyEvent(Request);
