namespace Just_Lilith.Core.Contracts;

public sealed record ChatRequest(RequestIdentity Identity, string InputText, string ProviderProfileId, long ProviderProfileRevision, long PersonaRevision, PoseSnapshot Pose, SpeechLanguage? SpeechLanguage, string? VoiceProfileId);
