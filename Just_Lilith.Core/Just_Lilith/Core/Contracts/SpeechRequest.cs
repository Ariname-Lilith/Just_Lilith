using System;

namespace Just_Lilith.Core.Contracts;

public sealed record SpeechRequest(RequestIdentity Identity, Guid MessageId, string Text, SpeechLanguage Language, string VoiceProfileId);
