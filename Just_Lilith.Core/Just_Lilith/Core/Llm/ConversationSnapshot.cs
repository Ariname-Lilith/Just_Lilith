using System;
using System.Collections.Generic;
using System.Linq;

namespace Just_Lilith.Core.Llm;

public sealed class ConversationSnapshot
{
	public string ProfileId { get; }

	public string ModelId { get; }

	public long Revision { get; }

	public IReadOnlyList<ConversationEvent> Events { get; }

	public IReadOnlyList<ConversationTurn> Turns { get; }

	internal ConversationSnapshot(string profileId, string modelId, long revision, IReadOnlyList<ConversationEvent> events)
	{
		ProfileId = profileId;
		ModelId = modelId;
		Revision = revision;
		Events = Array.AsReadOnly(events.Select((ConversationEvent e) => e with { }).ToArray());
		Turns = Array.AsReadOnly(Enumerable.Range(0, Events.Count / 2).Select(delegate(int index)
		{
			ConversationEvent conversationEvent = Events[index * 2];
			ConversationEvent conversationEvent2 = Events[index * 2 + 1];
			return new ConversationTurn(conversationEvent.Content, conversationEvent2.Content, conversationEvent.OccurredAtUtc, conversationEvent2.OccurredAtUtc);
		}).ToArray());
	}
}
