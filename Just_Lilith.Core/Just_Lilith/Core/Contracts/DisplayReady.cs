namespace Just_Lilith.Core.Contracts;

public sealed record DisplayReady(DisplayReply Reply) : ReplyEvent(Reply.Identity);
