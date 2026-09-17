using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Just_Lilith.Core.Llm;

public sealed record ActiveConversationSnapshot(string SessionId, ConversationSnapshot Conversation)
{
	public string DisplayName { get; init; } = "";

	public string ActivationToken { get; init; } = "";

	public IReadOnlyList<DreamMemoryEntry> ShortTermMemories
	{
		get
		{
			return shortTermMemories;
		}
		init
		{
			shortTermMemories = Array.AsReadOnly(value.Select((DreamMemoryEntry item) => item with { }).ToArray());
		}
	}

	public IReadOnlyList<DreamMemoryEntry> LongTermMemories
	{
		get
		{
			return longTermMemories;
		}
		init
		{
			longTermMemories = Array.AsReadOnly(value.Select((DreamMemoryEntry item) => item with { }).ToArray());
		}
	}

	public string EventsFingerprint => Fingerprint(Conversation.Events, ShortTermMemories, LongTermMemories);

	private IReadOnlyList<DreamMemoryEntry> shortTermMemories = Array.Empty<DreamMemoryEntry>();

	private IReadOnlyList<DreamMemoryEntry> longTermMemories = Array.Empty<DreamMemoryEntry>();

	private static string Fingerprint(IReadOnlyList<ConversationEvent> events, IReadOnlyList<DreamMemoryEntry> shortTerm, IReadOnlyList<DreamMemoryEntry> longTerm)
	{
		using IncrementalHash incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		Span<byte> span = stackalloc byte[13];
		AppendNumber(incrementalHash, events.Count);
		foreach (ConversationEvent @event in events)
		{
			byte[] bytes = Encoding.UTF8.GetBytes(@event.Content);
			span[0] = ((@event.Type != ConversationEventType.UserInput) ? ((byte)1) : ((byte)0));
			BinaryPrimitives.WriteInt32LittleEndian(span.Slice(1, 4), bytes.Length);
			BinaryPrimitives.WriteInt64LittleEndian(span.Slice(5, 8), @event.OccurredAtUtc.UtcTicks);
			incrementalHash.AppendData(span);
			incrementalHash.AppendData(bytes);
		}
		AppendMemories(incrementalHash, shortTerm);
		AppendMemories(incrementalHash, longTerm);
		return Convert.ToHexString(incrementalHash.GetHashAndReset()).ToLowerInvariant();
	}

	private static void AppendMemories(IncrementalHash hash, IReadOnlyList<DreamMemoryEntry> memories)
	{
		AppendNumber(hash, memories.Count);
		foreach (DreamMemoryEntry memory in memories)
		{
			AppendText(hash, memory.Id);
			AppendText(hash, memory.Content);
			AppendNumber(hash, memory.CreatedAtUtc.UtcTicks);
			AppendNumber(hash, memory.SourceStartAtUtc.UtcTicks);
			AppendNumber(hash, memory.SourceEndAtUtc.UtcTicks);
			AppendNumber(hash, memory.SourceTurnCount);
		}
	}

	private static void AppendText(IncrementalHash hash, string value)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(value);
		AppendNumber(hash, bytes.Length);
		hash.AppendData(bytes);
	}

	private static void AppendNumber(IncrementalHash hash, long value)
	{
		Span<byte> span = stackalloc byte[8];
		BinaryPrimitives.WriteInt64LittleEndian(span, value);
		hash.AppendData(span);
	}
}
