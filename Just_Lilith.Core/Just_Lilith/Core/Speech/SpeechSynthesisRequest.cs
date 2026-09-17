using System;
using Just_Lilith.Core.Contracts;

namespace Just_Lilith.Core.Speech;

public sealed record SpeechSynthesisRequest(Guid RequestId, string Text, SpeechLanguage Language, SpeechDirective Directive);
