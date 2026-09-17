using System;

namespace Just_Lilith.Core.Llm;

public sealed record DreamMemoryEntry(string Id, string Content, DateTimeOffset CreatedAtUtc, DateTimeOffset SourceStartAtUtc, DateTimeOffset SourceEndAtUtc, long SourceTurnCount);
