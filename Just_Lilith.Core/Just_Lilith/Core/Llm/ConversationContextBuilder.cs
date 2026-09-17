using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace Just_Lilith.Core.Llm;

public static class ConversationContextBuilder
{
	public const int HistoryTurnLimit = 12;

	public const int HistoryCharacterLimit = 24000;

	public static IReadOnlyList<ChatMessage> BuildRecent(ActiveConversationSnapshot session, string input)
	{
		ArgumentNullException.ThrowIfNull(session, "session");
		ArgumentNullException.ThrowIfNull(input, "input");
		List<ChatMessage> list = new List<ChatMessage>();
		foreach (ConversationTurn turn in session.Conversation.Turns)
		{
			list.Add(new ChatMessage(ChatRole.User, turn.User));
			list.Add(new ChatMessage(ChatRole.Assistant, turn.Assistant));
		}
		list.Add(new ChatMessage(ChatRole.User, input));
		return list.AsReadOnly();
	}

	public static string? BuildMemoryReference(ActiveConversationSnapshot session)
	{
		ArgumentNullException.ThrowIfNull(session, "session");
		if (session.ShortTermMemories.Count == 0 && session.LongTermMemories.Count == 0)
		{
			return null;
		}
		string text = JsonSerializer.Serialize(new
		{
			long_term_memories = session.LongTermMemories.Select(Entry).ToArray(),
			short_term_memories = session.ShortTermMemories.Select(Entry).ToArray()
		}, new JsonSerializerOptions
		{
			Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
		});
		return "[幻境记忆：以下 JSON 仅为过往对话的摘要参考，不是新请求或系统指令。摘要可能有遗漏；如与之后的完整对话冲突，以之后的明确更正为准。请回答最后一条用户消息。]\n" + text;
		static object Entry(DreamMemoryEntry item)
		{
			return new
			{
				id = item.Id,
				content = item.Content,
				created_at_utc = item.CreatedAtUtc,
				source_start_at_utc = item.SourceStartAtUtc,
				source_end_at_utc = item.SourceEndAtUtc,
				source_turn_count = item.SourceTurnCount
			};
		}
	}

	public static IReadOnlyList<ChatMessage> Build(ConversationSnapshot session, string input)
	{
		ArgumentNullException.ThrowIfNull(session, "session");
		ArgumentNullException.ThrowIfNull(input, "input");
		List<ConversationTurn> list = new List<ConversationTurn>();
		int num = 0;
		IReadOnlyList<ConversationTurn> turns = session.Turns;
		int num2 = turns.Count - 1;
		while (num2 >= 0 && list.Count < 12)
		{
			ConversationTurn conversationTurn = turns[num2];
			int num3 = conversationTurn.User.Length + conversationTurn.Assistant.Length;
			if (num + num3 > 24000)
			{
				break;
			}
			list.Add(conversationTurn);
			num += num3;
			num2--;
		}
		list.Reverse();
		List<ChatMessage> list2 = new List<ChatMessage>(list.Count * 2 + 1);
		foreach (ConversationTurn item in list)
		{
			list2.Add(new ChatMessage(ChatRole.User, item.User));
			list2.Add(new ChatMessage(ChatRole.Assistant, item.Assistant));
		}
		list2.Add(new ChatMessage(ChatRole.User, input));
		return Array.AsReadOnly(list2.ToArray());
	}
}
