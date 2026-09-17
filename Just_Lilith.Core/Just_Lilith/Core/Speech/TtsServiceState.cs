namespace Just_Lilith.Core.Speech;

public sealed record TtsServiceState(TtsServicePhase Phase, string Message, bool Owned = false);
