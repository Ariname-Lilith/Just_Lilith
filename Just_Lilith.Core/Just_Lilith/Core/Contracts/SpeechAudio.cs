using System;

namespace Just_Lilith.Core.Contracts;

public sealed record SpeechAudio(RequestIdentity Identity, Guid MessageId, int SegmentIndex, int SampleRate, int Channels, ReadOnlyMemory<byte> FileData);
