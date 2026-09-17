namespace Just_Lilith.Core.Contracts;

public sealed record SpeechReady(SpeechRequest Request) : ReplyEvent(Request.Identity);
