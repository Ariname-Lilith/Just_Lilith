using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace Just_Lilith.Core.Llm;

public sealed class ActiveConversationWorkspaceStore
{
	private sealed record PointerFile
	{
		[JsonPropertyName("schema_version")]
		public int SchemaVersion { get; init; }

		[JsonPropertyName("session_id")]
		public string SessionId { get; init; } = "";

		[JsonPropertyName("activation_token")]
		public string ActivationToken { get; init; } = "";
	}

	private sealed record SessionFile
	{
		[JsonPropertyName("schema_version")]
		public int SchemaVersion { get; init; }

		[JsonPropertyName("session_id")]
		public string SessionId { get; init; } = "";

		[JsonPropertyName("display_name")]
		public string DisplayName { get; init; } = "";

		[JsonPropertyName("revision")]
		public long Revision { get; init; }

		[JsonPropertyName("events")]
		public EventFile[] Events { get; init; } = Array.Empty<EventFile>();

		[JsonPropertyName("short_term_memories")]
		public MemoryFile[] ShortTermMemories { get; init; } = Array.Empty<MemoryFile>();

		[JsonPropertyName("long_term_memories")]
		public MemoryFile[] LongTermMemories { get; init; } = Array.Empty<MemoryFile>();
	}

	private sealed record MemoryFile
	{
		[JsonPropertyName("id")]
		public string Id { get; init; } = "";

		[JsonPropertyName("content")]
		public string Content { get; init; } = "";

		[JsonPropertyName("created_at_utc")]
		public string CreatedAtUtc { get; init; } = "";

		[JsonPropertyName("source_start_at_utc")]
		public string SourceStartAtUtc { get; init; } = "";

		[JsonPropertyName("source_end_at_utc")]
		public string SourceEndAtUtc { get; init; } = "";

		[JsonPropertyName("source_turn_count")]
		public long SourceTurnCount { get; init; }
	}

	private sealed record EventFile
	{
		[JsonPropertyName("event_type")]
		public string EventType { get; init; } = "";

		[JsonPropertyName("content")]
		public string Content { get; init; } = "";

		[JsonPropertyName("occurred_at_utc")]
		public string OccurredAtUtc { get; init; } = "";
	}

	private const int SessionSchemaVersion = 3;

	private const int PointerSchemaVersion = 3;

	private const int PreviousPointerSchemaVersion = 2;

	private const int PointerByteLimit = 4096;

	private const int MigrationFileCountLimit = 10000;

	private const int DisplayNameCharacterLimit = 64;

	private const string DefaultNamePrefix = "新建幻境";

	private static readonly DateTimeOffset EarliestEvent = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private static readonly TimeSpan MaximumFutureClockSkew = TimeSpan.FromHours(24.0);

	private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

	private static readonly ConcurrentDictionary<string, object> Gates = new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		MaxDepth = 16,
		WriteIndented = true,
		Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
	};

	private readonly string workspaceDirectory;

	private readonly string pointerPath;

	private readonly string sessionsDirectory;

	private readonly string previousSessionsDirectory;

	private readonly string legacySessionsDirectory;

	private readonly object gate;

	public ActiveConversationWorkspaceStore(string workspaceDirectory)
	{
		if (string.IsNullOrWhiteSpace(workspaceDirectory))
		{
			throw new LlmException("workspace_path", "对话工作区路径不正确。");
		}
		try
		{
			this.workspaceDirectory = Path.GetFullPath(workspaceDirectory);
		}
		catch (Exception ex) when (((ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) ? 1 : 0) != 0)
		{
			throw new LlmException("workspace_path", "对话工作区路径不正确。");
		}
		pointerPath = Path.Combine(this.workspaceDirectory, "active-session.json");
		sessionsDirectory = Path.Combine(this.workspaceDirectory, "sessions");
		previousSessionsDirectory = Path.Combine(this.workspaceDirectory, "sessions-v2");
		legacySessionsDirectory = sessionsDirectory;
		gate = Gates.GetOrAdd(pointerPath, (string _) => new object());
		lock (gate)
		{
			EnsureSessionsDirectoryMigrated();
		}
	}

	private void EnsureSessionsDirectoryMigrated()
	{
		if (!Directory.Exists(previousSessionsDirectory))
		{
			return;
		}
		try
		{
			if (!Directory.Exists(sessionsDirectory))
			{
				try
				{
					Directory.Move(previousSessionsDirectory, sessionsDirectory);
					return;
				}
				catch (IOException) when (Directory.Exists(sessionsDirectory))
				{
				}
			}
			string[] array = Directory.EnumerateFiles(previousSessionsDirectory, "session-*.json", SearchOption.TopDirectoryOnly).Where(delegate(string path)
			{
				string fileName = Path.GetFileName(path);
				return fileName.StartsWith("session-", StringComparison.Ordinal) && fileName.EndsWith(".json", StringComparison.Ordinal);
			}).ToArray();
			if (array.Length > 10000)
			{
				throw new LlmException("conversation_limit", "待迁移会话文件数量超过上限；现有文件均已保留。");
			}
			string[] array2 = array;
			foreach (string text in array2)
			{
				string text2 = Path.Combine(sessionsDirectory, Path.GetFileName(text));
				if (File.Exists(text2) && !FilesAreIdentical(text, text2))
				{
					throw new LlmException("conversation_migration_conflict", "sessions 与 sessions-v2 中存在同名但内容不同的会话；两个文件均已保留，请手动处理：" + Path.GetFileName(text));
				}
			}
			Directory.CreateDirectory(sessionsDirectory);
			array2 = array;
			foreach (string text3 in array2)
			{
				string text4 = Path.Combine(sessionsDirectory, Path.GetFileName(text3));
				if (File.Exists(text4))
				{
					if (!FilesAreIdentical(text3, text4))
					{
						throw new LlmException("conversation_migration_conflict", "迁移期间会话文件发生变化；两个文件均已保留，请重试：" + Path.GetFileName(text3));
					}
					File.Delete(text3);
				}
				else
				{
					File.Move(text3, text4);
				}
			}
			if (!Directory.EnumerateFileSystemEntries(previousSessionsDirectory).Any())
			{
				Directory.Delete(previousSessionsDirectory);
			}
		}
		catch (LlmException)
		{
			throw;
		}
		catch (Exception ex3) when (((ex3 is IOException || ex3 is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			throw new LlmException("conversation_migration", "迁移 sessions-v2 到 sessions 失败；没有覆盖会话文件。原因：" + ex3.GetType().Name);
		}
	}

	private static bool FilesAreIdentical(string leftPath, string rightPath)
	{
		using FileStream fileStream = new FileStream(leftPath, FileMode.Open, FileAccess.Read, FileShare.Read);
		using FileStream fileStream2 = new FileStream(rightPath, FileMode.Open, FileAccess.Read, FileShare.Read);
		if (fileStream.Length != fileStream2.Length)
		{
			return false;
		}
		byte[] array = new byte[8192];
		byte[] array2 = new byte[8192];
		int num;
		int num2;
		do
		{
			num = fileStream.Read(array, 0, array.Length);
			num2 = fileStream2.Read(array2, 0, array2.Length);
			if (num != num2)
			{
				return false;
			}
			if (num == 0)
			{
				return true;
			}
		}
		while (array.AsSpan(0, num).SequenceEqual(array2.AsSpan(0, num2)));
		return false;
	}

	public ActiveConversationSnapshot LoadOrCreate(string profileId, string modelId, string baseUrl, string credentialScope)
	{
		lock (gate)
		{
			if (File.Exists(pointerPath))
			{
				return ReadActive();
			}
			ConversationSnapshot conversationSnapshot = new ConversationWorkspaceStore(workspaceDirectory).LoadSession(profileId, modelId, baseUrl, credentialScope);
			ConversationSnapshot conversationSnapshot2 = ((conversationSnapshot.Events.Count != 0) ? conversationSnapshot : (FindNewestLegacy() ?? conversationSnapshot));
			return CreateAndActivate(conversationSnapshot2.Revision, conversationSnapshot2.Events, NewActivationToken());
		}
	}

	public string GetActiveSessionFilePath()
	{
		lock (gate)
		{
			ActiveConversationSnapshot activeConversationSnapshot = ReadActive();
			return SessionPath(activeConversationSnapshot.SessionId);
		}
	}

	public ActiveConversationSnapshot EnsureCanAppend(string sessionId, long expectedRevision, string user)
	{
		ValidateSessionId(sessionId);
		ValidateTurnText(user);
		if (expectedRevision < 0)
		{
			throw RevisionError();
		}
		lock (gate)
		{
			ActiveConversationSnapshot activeConversationSnapshot = ReadActiveAndCheckId(sessionId);
			ValidateAppendCapacity(activeConversationSnapshot, expectedRevision, user);
			return activeConversationSnapshot;
		}
	}

	public ActiveConversationSnapshot AppendCompletedTurn(string sessionId, long expectedRevision, string user, DateTimeOffset userAt, string assistant, DateTimeOffset assistantAt, string? expectedEventsFingerprint = null, string? expectedActivationToken = null)
	{
		ValidateSessionId(sessionId);
		ValidateTurnText(user);
		ValidateTurnText(assistant);
		ValidateTimes(userAt, assistantAt);
		if (expectedRevision < 0)
		{
			throw RevisionError();
		}
		lock (gate)
		{
			ActiveConversationSnapshot activeConversationSnapshot = ReadActiveAndCheckId(sessionId);
			if (expectedActivationToken != null && !string.Equals(activeConversationSnapshot.ActivationToken, expectedActivationToken, StringComparison.Ordinal))
			{
				throw new LlmException("conversation_changed", "活动会话已切换；旧请求的回复未写入新会话。");
			}
			ValidateAppendCapacity(activeConversationSnapshot, expectedRevision, user);
			if (expectedEventsFingerprint != null && !string.Equals(activeConversationSnapshot.EventsFingerprint, expectedEventsFingerprint, StringComparison.Ordinal))
			{
				throw new LlmException("conversation_changed", "会话内容已被编辑，请重新载入后发送。");
			}
			DateTimeOffset? dateTimeOffset = LastSourceTime(activeConversationSnapshot);
			if (dateTimeOffset.HasValue)
			{
				DateTimeOffset valueOrDefault = dateTimeOffset.GetValueOrDefault();
				if (valueOrDefault > userAt)
				{
					throw new LlmException("conversation_time", "新消息时间早于已有会话；原会话保持不变。");
				}
			}
			long revision;
			try
			{
				revision = checked(activeConversationSnapshot.Conversation.Revision + 1);
			}
			catch (OverflowException)
			{
				throw RevisionError();
			}
			ConversationEvent[] events = activeConversationSnapshot.Conversation.Events.Concat(new ConversationEvent[2]
			{
				new ConversationEvent(ConversationEventType.UserInput, user, userAt),
				new ConversationEvent(ConversationEventType.LlmReply, assistant, assistantAt)
			}).ToArray();
			ActiveConversationSnapshot activeConversationSnapshot2 = Snapshot(sessionId, revision, events, activeConversationSnapshot.DisplayName)with
			{
				ActivationToken = activeConversationSnapshot.ActivationToken,
				ShortTermMemories = activeConversationSnapshot.ShortTermMemories,
				LongTermMemories = activeConversationSnapshot.LongTermMemories
			};
			WriteSession(activeConversationSnapshot2);
			return activeConversationSnapshot2;
		}
	}

	public ActiveConversationSnapshot ReloadForMemory(ActiveConversationSnapshot expected)
	{
		ArgumentNullException.ThrowIfNull(expected, "expected");
		ValidateSessionId(expected.SessionId);
		lock (gate)
		{
			ActiveConversationSnapshot activeConversationSnapshot = ReadActiveAndCheckId(expected.SessionId);
			ValidateActivation(activeConversationSnapshot, expected);
			return activeConversationSnapshot;
		}
	}

	public ActiveConversationSnapshot CommitShortTermMemory(ActiveConversationSnapshot expected, string summary, DateTimeOffset createdAtUtc)
	{
		ArgumentNullException.ThrowIfNull(expected, "expected");
		DreamMemoryPolicy.ValidateSummary(summary, 150);
		ValidateSessionId(expected.SessionId);
		lock (gate)
		{
			ActiveConversationSnapshot activeConversationSnapshot = ReadExpectedMemoryState(expected);
			if (activeConversationSnapshot.Conversation.Turns.Count < 30)
			{
				throw new LlmException("memory_not_due", "近期对话尚未达到记忆整理阈值；原记录保持不变。");
			}
			if (activeConversationSnapshot.ShortTermMemories.Count >= 20)
			{
				throw MemoryPendingError();
			}
			ConversationEvent[] array = activeConversationSnapshot.Conversation.Events.Take(40).ToArray();
			ValidateTimes(createdAtUtc, createdAtUtc);
			DreamMemoryEntry element = new DreamMemoryEntry(Guid.NewGuid().ToString("N"), summary, createdAtUtc, array[0].OccurredAtUtc, array[^1].OccurredAtUtc, 20L);
			ActiveConversationSnapshot activeConversationSnapshot2 = Snapshot(activeConversationSnapshot.SessionId, NextRevision(activeConversationSnapshot), activeConversationSnapshot.Conversation.Events.Skip(array.Length).ToArray(), activeConversationSnapshot.DisplayName)with
			{
				ActivationToken = activeConversationSnapshot.ActivationToken,
				ShortTermMemories = activeConversationSnapshot.ShortTermMemories.Append(element).ToArray(),
				LongTermMemories = activeConversationSnapshot.LongTermMemories
			};
			WriteSession(activeConversationSnapshot2);
			return activeConversationSnapshot2;
		}
	}

	public ActiveConversationSnapshot CommitLongTermMemory(ActiveConversationSnapshot expected, string summary, DateTimeOffset createdAtUtc)
	{
		ArgumentNullException.ThrowIfNull(expected, "expected");
		DreamMemoryPolicy.ValidateSummary(summary, 500);
		ValidateSessionId(expected.SessionId);
		lock (gate)
		{
			ActiveConversationSnapshot activeConversationSnapshot = ReadExpectedMemoryState(expected);
			if (activeConversationSnapshot.ShortTermMemories.Count < 20)
			{
				throw new LlmException("memory_not_due", "短期记忆尚未达到整理阈值；原记录保持不变。");
			}
			DreamMemoryEntry[] array = activeConversationSnapshot.ShortTermMemories.Take(10).ToArray();
			ValidateTimes(createdAtUtc, createdAtUtc);
			long sourceTurnCount;
			try
			{
				sourceTurnCount = array.Aggregate(0L, (long sum, DreamMemoryEntry source) => checked(sum + source.SourceTurnCount));
			}
			catch (OverflowException)
			{
				throw FormatError();
			}
			DreamMemoryEntry element = new DreamMemoryEntry(Guid.NewGuid().ToString("N"), summary, createdAtUtc, array[0].SourceStartAtUtc, array[^1].SourceEndAtUtc, sourceTurnCount);
			ActiveConversationSnapshot activeConversationSnapshot2 = Snapshot(activeConversationSnapshot.SessionId, NextRevision(activeConversationSnapshot), activeConversationSnapshot.Conversation.Events, activeConversationSnapshot.DisplayName)with
			{
				ActivationToken = activeConversationSnapshot.ActivationToken,
				ShortTermMemories = activeConversationSnapshot.ShortTermMemories.Skip(array.Length).ToArray(),
				LongTermMemories = activeConversationSnapshot.LongTermMemories.Append(element).ToArray()
			};
			WriteSession(activeConversationSnapshot2);
			return activeConversationSnapshot2;
		}
	}

	private ActiveConversationSnapshot ReadExpectedMemoryState(ActiveConversationSnapshot expected)
	{
		ActiveConversationSnapshot activeConversationSnapshot = ReadActiveAndCheckId(expected.SessionId);
		ValidateActivation(activeConversationSnapshot, expected);
		if (activeConversationSnapshot.Conversation.Revision != expected.Conversation.Revision || !string.Equals(activeConversationSnapshot.EventsFingerprint, expected.EventsFingerprint, StringComparison.Ordinal))
		{
			throw new LlmException("conversation_changed", "会话内容已更新；旧记忆摘要未提交，原记录保持不变。");
		}
		return activeConversationSnapshot;
	}

	private static void ValidateActivation(ActiveConversationSnapshot current, ActiveConversationSnapshot expected)
	{
		if (!string.Equals(current.ActivationToken, expected.ActivationToken, StringComparison.Ordinal))
		{
			throw new LlmException("conversation_changed", "活动会话已切换；旧记忆摘要未写入新会话。");
		}
	}

	private static long NextRevision(ActiveConversationSnapshot current)
	{
		try
		{
			return checked(current.Conversation.Revision + 1);
		}
		catch (OverflowException)
		{
			throw RevisionError();
		}
	}

	private static DateTimeOffset? LastSourceTime(ActiveConversationSnapshot snapshot)
	{
		if (snapshot.Conversation.Events.Count <= 0)
		{
			if (snapshot.ShortTermMemories.Count <= 0)
			{
				if (snapshot.LongTermMemories.Count <= 0)
				{
					return null;
				}
				IReadOnlyList<DreamMemoryEntry> longTermMemories = snapshot.LongTermMemories;
				return longTermMemories[longTermMemories.Count - 1].SourceEndAtUtc;
			}
			IReadOnlyList<DreamMemoryEntry> shortTermMemories = snapshot.ShortTermMemories;
			return shortTermMemories[shortTermMemories.Count - 1].SourceEndAtUtc;
		}
		IReadOnlyList<ConversationEvent> events = snapshot.Conversation.Events;
		return events[events.Count - 1].OccurredAtUtc;
	}

	public IReadOnlyList<ActiveConversationInfo> ListSessions()
	{
		lock (gate)
		{
			string text = (File.Exists(pointerPath) ? ReadPointer().SessionId : null);
			if (text == null)
			{
				return Array.Empty<ActiveConversationInfo>();
			}
			List<ActiveConversationInfo> list = new List<ActiveConversationInfo>();
			try
			{
				string[] array = SessionPaths();
				Dictionary<string, string> dictionary = FallbackNames(array);
				string[] array2 = array;
				foreach (string text2 in array2)
				{
					string fileName = Path.GetFileName(text2);
					string text3 = fileName.Substring("session-".Length, fileName.Length - "session-".Length - ".json".Length);
					bool isActive = string.Equals(text3, text, StringComparison.Ordinal);
					string text4 = dictionary[text2];
					try
					{
						ValidateSessionId(text3);
						ActiveConversationSnapshot activeConversationSnapshot = ReadSession(text3, text4);
						list.Add(new ActiveConversationInfo(text3, activeConversationSnapshot.Conversation.Turns.Count, LastSourceTime(activeConversationSnapshot), (activeConversationSnapshot.Conversation.Events.Count == 0) ? null : activeConversationSnapshot.Conversation.Events[0].Content, isActive, null)
						{
							DisplayName = activeConversationSnapshot.DisplayName
						});
					}
					catch (LlmException ex)
					{
						list.Add(new ActiveConversationInfo(text3, null, null, null, isActive, ex.Code)
						{
							DisplayName = text4
						});
					}
				}
				if (text != null && !list.Any((ActiveConversationInfo session) => session.IsActive))
				{
					list.Add(new ActiveConversationInfo(text, null, null, null, IsActive: true, "conversation_read")
					{
						DisplayName = "新建幻境" + (array.Length + 1).ToString(CultureInfo.InvariantCulture)
					});
				}
			}
			catch (LlmException)
			{
				throw;
			}
			catch (Exception ex3) when (((ex3 is IOException || ex3 is UnauthorizedAccessException) ? 1 : 0) != 0)
			{
				throw new LlmException("conversation_read", "列出会话失败；原文件保持不变。");
			}
			return list.OrderByDescending((ActiveConversationInfo session) => session.LastActivityAtUtc).ThenBy<ActiveConversationInfo, string>((ActiveConversationInfo session) => session.SessionId, StringComparer.Ordinal).ToArray();
		}
	}

	public ActiveConversationSnapshot SwitchSession(string targetSessionId, string? expectedActiveSessionId = null)
	{
		ValidateSessionId(targetSessionId);
		if (expectedActiveSessionId != null)
		{
			ValidateSessionId(expectedActiveSessionId);
		}
		lock (gate)
		{
			(string, string) tuple = ReadPointer();
			if (expectedActiveSessionId != null && !string.Equals(tuple.Item1, expectedActiveSessionId, StringComparison.Ordinal))
			{
				throw new LlmException("conversation_changed", "活动会话已切换，请重新载入后选择。");
			}
			ActiveConversationSnapshot activeConversationSnapshot = ReadSession(targetSessionId)with
			{
				ActivationToken = (string.Equals(tuple.Item1, targetSessionId, StringComparison.Ordinal) ? tuple.Item2 : NewActivationToken(tuple.Item2))
			};
			if (string.Equals(tuple.Item1, targetSessionId, StringComparison.Ordinal))
			{
				return activeConversationSnapshot;
			}
			WritePointer(activeConversationSnapshot);
			return activeConversationSnapshot;
		}
	}

	public ActiveConversationSnapshot StartNewSession()
	{
		lock (gate)
		{
			string activationToken = (File.Exists(pointerPath) ? NewActivationToken(ReadPointer().ActivationToken) : NewActivationToken());
			return CreateAndActivate(0L, Array.Empty<ConversationEvent>(), activationToken);
		}
	}

	public ActiveConversationSnapshot RenameSession(string sessionId, string displayName)
	{
		ValidateSessionId(sessionId);
		string text = NormalizeDisplayName(displayName);
		lock (gate)
		{
			(string, string) tuple = ReadPointer();
			ActiveConversationSnapshot activeConversationSnapshot = ReadSession(sessionId);
			ActiveConversationSnapshot activeConversationSnapshot2 = activeConversationSnapshot with
			{
				DisplayName = text,
				ActivationToken = (string.Equals(tuple.Item1, sessionId, StringComparison.Ordinal) ? tuple.Item2 : "")
			};
			if (!string.Equals(activeConversationSnapshot.DisplayName, text, StringComparison.Ordinal))
			{
				WriteSession(activeConversationSnapshot2);
			}
			return activeConversationSnapshot2;
		}
	}

	public ActiveConversationSnapshot DeleteSession(string sessionId, string? expectedActiveSessionId = null)
	{
		ValidateSessionId(sessionId);
		if (expectedActiveSessionId != null)
		{
			ValidateSessionId(expectedActiveSessionId);
		}
		lock (gate)
		{
			(string, string) tuple = ReadPointer();
			if (expectedActiveSessionId != null && !string.Equals(tuple.Item1, expectedActiveSessionId, StringComparison.Ordinal))
			{
				throw new LlmException("conversation_changed", "活动会话已切换，请重新载入后删除。");
			}
			string text = SessionPath(sessionId);
			if (!File.Exists(text))
			{
				throw new LlmException("conversation_changed", "待删除的幻境已不存在，请重新载入。");
			}
			if (!string.Equals(tuple.Item1, sessionId, StringComparison.Ordinal))
			{
				ActiveConversationSnapshot result = ReadActive();
				DeleteSessionFile(text);
				return result;
			}
			string[] array = SessionPaths();
			Dictionary<string, string> dictionary = FallbackNames(array);
			ActiveConversationSnapshot activeConversationSnapshot = null;
			string[] array2 = array;
			bool flag = default(bool);
			foreach (string text2 in array2)
			{
				if (string.Equals(text2, text, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				string fileName = Path.GetFileName(text2);
				string id = fileName.Substring("session-".Length, fileName.Length - "session-".Length - ".json".Length);
				try
				{
					ValidateSessionId(id);
					ActiveConversationSnapshot activeConversationSnapshot2 = ReadSession(id, dictionary[text2]);
					if ((object)activeConversationSnapshot == null || PreferReplacement(activeConversationSnapshot2, activeConversationSnapshot))
					{
						activeConversationSnapshot = activeConversationSnapshot2;
					}
				}
				catch (LlmException ex) when (((Func<bool>)delegate
				{
					// Could not convert BlockContainer to single expression
					switch (ex.Code)
					{
					case "conversation_id":
					case "conversation_format":
					case "conversation_version":
					case "conversation_read":
					case "conversation_limit":
					case "conversation_name":
						flag = true;
						break;
					default:
						flag = false;
						break;
					}
					return flag;
				}).Invoke())
				{
				}
			}
			bool flag2 = (object)activeConversationSnapshot == null;
			if ((object)activeConversationSnapshot == null)
			{
				activeConversationSnapshot = Snapshot(Guid.NewGuid().ToString("N"), 0L, Array.Empty<ConversationEvent>(), NextDefaultName());
			}
			activeConversationSnapshot = activeConversationSnapshot with
			{
				ActivationToken = NewActivationToken(tuple.Item2)
			};
			if (flag2)
			{
				WriteSession(activeConversationSnapshot);
			}
			try
			{
				WritePointer(activeConversationSnapshot);
			}
			catch (LlmException)
			{
				if (flag2)
				{
					try
					{
						File.Delete(SessionPath(activeConversationSnapshot.SessionId));
					}
					catch (Exception ex3) when (((ex3 is IOException || ex3 is UnauthorizedAccessException) ? 1 : 0) != 0)
					{
					}
				}
				throw;
			}
			try
			{
				DeleteSessionFile(text);
			}
			catch (LlmException)
			{
				try
				{
					WritePointer(tuple.Item1, tuple.Item2);
				}
				catch (LlmException)
				{
				}
				if (flag2)
				{
					try
					{
						File.Delete(SessionPath(activeConversationSnapshot.SessionId));
					}
					catch (Exception ex6) when (((ex6 is IOException || ex6 is UnauthorizedAccessException) ? 1 : 0) != 0)
					{
					}
				}
				throw;
			}
			return activeConversationSnapshot;
		}
	}

	private static bool PreferReplacement(ActiveConversationSnapshot candidate, ActiveConversationSnapshot current)
	{
		DateTimeOffset dateTimeOffset = LastSourceTime(candidate) ?? DateTimeOffset.MinValue;
		DateTimeOffset dateTimeOffset2 = LastSourceTime(current) ?? DateTimeOffset.MinValue;
		if (!(dateTimeOffset > dateTimeOffset2))
		{
			if (dateTimeOffset == dateTimeOffset2)
			{
				return string.CompareOrdinal(candidate.SessionId, current.SessionId) < 0;
			}
			return false;
		}
		return true;
	}

	private static void DeleteSessionFile(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			throw new LlmException("conversation_delete", "删除幻境失败；原会话文件保持不变。");
		}
	}

	private ActiveConversationSnapshot CreateAndActivate(long revision, IReadOnlyList<ConversationEvent> events, string activationToken)
	{
		Guid guid = Guid.NewGuid();
		ActiveConversationSnapshot activeConversationSnapshot = Snapshot(guid.ToString("N"), revision, events, NextDefaultName())with
		{
			ActivationToken = activationToken
		};
		WriteSession(activeConversationSnapshot);
		WritePointer(activeConversationSnapshot);
		return activeConversationSnapshot;
	}

	private void WritePointer(ActiveConversationSnapshot session)
	{
		WritePointer(session.SessionId, session.ActivationToken);
	}

	private void WritePointer(string sessionId, string activationToken)
	{
		WriteAtomic(pointerPath, JsonSerializer.SerializeToUtf8Bytes(new PointerFile
		{
			SchemaVersion = 3,
			SessionId = sessionId,
			ActivationToken = activationToken
		}, JsonOptions), 4096);
	}

	private static string NewActivationToken(string? previous = null)
	{
		string text;
		do
		{
			text = Guid.NewGuid().ToString("N");
		}
		while (string.Equals(text, previous, StringComparison.Ordinal));
		return text;
	}

	private static bool IsActivationToken(string token)
	{
		if (token.Length == 32)
		{
			return token.All(delegate(char value)
			{
				switch (value)
				{
				case '0':
				case '1':
				case '2':
				case '3':
				case '4':
				case '5':
				case '6':
				case '7':
				case '8':
				case '9':
				case 'a':
				case 'b':
				case 'c':
				case 'd':
				case 'e':
				case 'f':
					return true;
				default:
					return false;
				}
			});
		}
		return false;
	}

	private ActiveConversationSnapshot ReadActiveAndCheckId(string sessionId)
	{
		ActiveConversationSnapshot activeConversationSnapshot = ReadActive();
		if (!string.Equals(activeConversationSnapshot.SessionId, sessionId, StringComparison.Ordinal))
		{
			throw new LlmException("conversation_changed", "活动会话已切换；旧请求的回复未写入新会话。");
		}
		return activeConversationSnapshot;
	}

	private ActiveConversationSnapshot ReadActive()
	{
		(string, string) tuple = ReadPointer();
		return ReadSession(tuple.Item1)with
		{
			ActivationToken = tuple.Item2
		};
	}

	private (string SessionId, string ActivationToken) ReadPointer()
	{
		try
		{
			using JsonDocument jsonDocument = ParseFile(pointerPath, 4096);
			JsonElement rootElement = jsonDocument.RootElement;
			if (rootElement.ValueKind != JsonValueKind.Object || !rootElement.TryGetProperty("schema_version", out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var value2))
			{
				throw FormatError();
			}
			string text = null;
			switch (value2)
			{
			case 1:
				RequireProperties(rootElement, "schema_version", "session_id");
				break;
			case 2:
			{
				RequireProperties(rootElement, "schema_version", "session_id", "activation_generation");
				JsonElement property2 = rootElement.GetProperty("activation_generation");
				if (property2.ValueKind != JsonValueKind.Number || !property2.TryGetInt64(out var value3) || value3 < 1)
				{
					throw FormatError();
				}
				break;
			}
			case 3:
			{
				RequireProperties(rootElement, "schema_version", "session_id", "activation_token");
				JsonElement property = rootElement.GetProperty("activation_token");
				if (property.ValueKind != JsonValueKind.String || !IsActivationToken(property.GetString()))
				{
					throw FormatError();
				}
				text = property.GetString();
				break;
			}
			default:
				throw FormatError();
			}
			JsonElement property3 = rootElement.GetProperty("session_id");
			if (property3.ValueKind != JsonValueKind.String)
			{
				throw FormatError();
			}
			string text2 = property3.GetString();
			ValidateSessionId(text2);
			if (text == null)
			{
				text = NewActivationToken();
				WritePointer(text2, text);
			}
			return (SessionId: text2, ActivationToken: text);
		}
		catch (JsonException)
		{
			throw FormatError();
		}
		catch (LlmException ex2) when (ex2.Code == "conversation_id")
		{
			throw FormatError();
		}
		catch (LlmException)
		{
			throw;
		}
		catch (Exception ex4) when (((ex4 is IOException || ex4 is UnauthorizedAccessException || ex4 is DecoderFallbackException) ? 1 : 0) != 0)
		{
			throw new LlmException("conversation_read", "读取活动会话失败；原文件保持不变。");
		}
	}

	private ActiveConversationSnapshot ReadSession(string id, string? fallbackName = null)
	{
		try
		{
			using JsonDocument jsonDocument = ParseFile(SessionPath(id));
			JsonElement rootElement = jsonDocument.RootElement;
			if (rootElement.ValueKind != JsonValueKind.Object || !rootElement.TryGetProperty("schema_version", out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var value2))
			{
				throw FormatError();
			}
			if (value2 != 2 && value2 != 3)
			{
				throw new LlmException("conversation_version", "会话文件版本不受支持；文件保持不变。");
			}
			RequireSessionProperties(rootElement, value2);
			JsonElement property = rootElement.GetProperty("session_id");
			if (property.ValueKind != JsonValueKind.String || !string.Equals(property.GetString(), id, StringComparison.Ordinal))
			{
				throw FormatError();
			}
			long val = ReadRevision(rootElement);
			IReadOnlyList<ConversationEvent> readOnlyList = ReadEvents(rootElement.GetProperty("events"));
			string displayName = (rootElement.TryGetProperty("display_name", out var value3) ? ReadDisplayName(value3) : (fallbackName ?? FallbackName(id)));
			ActiveConversationSnapshot activeConversationSnapshot = Snapshot(id, Math.Max(val, readOnlyList.Count / 2), readOnlyList, displayName);
			if (value2 == 3)
			{
				HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
				activeConversationSnapshot = activeConversationSnapshot with
				{
					ShortTermMemories = ReadMemories(rootElement.GetProperty("short_term_memories"), 150, ids),
					LongTermMemories = ReadMemories(rootElement.GetProperty("long_term_memories"), 500, ids)
				};
				ValidateMemoryTimeline(activeConversationSnapshot);
			}
			return activeConversationSnapshot;
		}
		catch (JsonException)
		{
			throw FormatError();
		}
		catch (LlmException ex2) when (((Func<bool>)delegate
		{
			// Could not convert BlockContainer to single expression
			bool flag = default(bool);
			switch (ex2.Code)
			{
			case "conversation_text":
			case "conversation_time":
			case "conversation_name":
			case "memory_summary":
			case "conversation_id":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			return flag;
		}).Invoke())
		{
			throw FormatError();
		}
		catch (LlmException)
		{
			throw;
		}
		catch (Exception ex4) when (((ex4 is IOException || ex4 is UnauthorizedAccessException || ex4 is DecoderFallbackException) ? 1 : 0) != 0)
		{
			throw new LlmException("conversation_read", "读取会话失败；原文件保持不变。");
		}
	}

	private ConversationSnapshot? FindNewestLegacy()
	{
		try
		{
			if (!Directory.Exists(legacySessionsDirectory))
			{
				return null;
			}
			ConversationSnapshot conversationSnapshot = null;
			DateTimeOffset dateTimeOffset = DateTimeOffset.MinValue;
			int num = 0;
			foreach (string item in Directory.EnumerateFiles(legacySessionsDirectory, "chat-*.json"))
			{
				if (++num > 10000)
				{
					throw new LlmException("conversation_limit", "旧会话文件数量超过迁移上限；原文件保持不变。");
				}
				try
				{
					ConversationSnapshot conversationSnapshot2 = ReadLegacy(item);
					if (conversationSnapshot2.Turns.Count != 0)
					{
						IReadOnlyList<ConversationEvent> events = conversationSnapshot2.Events;
						DateTimeOffset occurredAtUtc = events[events.Count - 1].OccurredAtUtc;
						if (conversationSnapshot == null || occurredAtUtc > dateTimeOffset)
						{
							conversationSnapshot = conversationSnapshot2;
							dateTimeOffset = occurredAtUtc;
						}
					}
				}
				catch (Exception ex) when (((ex is LlmException || ex is JsonException || ex is IOException || ex is UnauthorizedAccessException || ex is DecoderFallbackException) ? 1 : 0) != 0)
				{
				}
			}
			return conversationSnapshot;
		}
		catch (LlmException)
		{
			throw;
		}
		catch (Exception ex3) when (((ex3 is IOException || ex3 is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			throw new LlmException("conversation_read", "扫描旧会话失败；原文件保持不变。");
		}
	}

	private static ConversationSnapshot ReadLegacy(string path)
	{
		using JsonDocument jsonDocument = ParseFile(path, 8388608);
		JsonElement rootElement = jsonDocument.RootElement;
		RequireProperties(rootElement, "schema_version", "revision", "profile_id", "model_id", "endpoint_fingerprint", "credential_fingerprint", "events");
		JsonElement property = rootElement.GetProperty("schema_version");
		if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var value) || value != 1)
		{
			throw FormatError();
		}
		long val = ReadRevision(rootElement);
		string text = ReadLegacyId(rootElement.GetProperty("profile_id"), 64);
		string text2 = ReadLegacyId(rootElement.GetProperty("model_id"), 256);
		string endpoint = ReadFingerprint(rootElement.GetProperty("endpoint_fingerprint"));
		string credential = ReadFingerprint(rootElement.GetProperty("credential_fingerprint"));
		if (!string.Equals(Path.GetFileName(path), "chat-" + LegacyDigest(text, text2, endpoint, credential) + ".json", StringComparison.OrdinalIgnoreCase))
		{
			throw FormatError();
		}
		IReadOnlyList<ConversationEvent> readOnlyList = ReadEvents(rootElement.GetProperty("events"));
		return new ConversationSnapshot(text, text2, Math.Max(val, readOnlyList.Count / 2), readOnlyList);
	}

	private static string LegacyDigest(string profile, string model, string endpoint, string credential)
	{
		byte[][] obj = new byte[4][]
		{
			StrictUtf8.GetBytes(profile),
			StrictUtf8.GetBytes(model),
			StrictUtf8.GetBytes(endpoint),
			StrictUtf8.GetBytes(credential)
		};
		byte[] array = new byte[obj.Sum((byte[] field) => field.Length + 4)];
		int num = 0;
		byte[][] array2 = obj;
		foreach (byte[] array3 in array2)
		{
			BinaryPrimitives.WriteInt32LittleEndian(array.AsSpan(num, 4), array3.Length);
			array3.CopyTo(array.AsSpan(num + 4));
			num += array3.Length + 4;
		}
		return Convert.ToHexString(SHA256.HashData(array)).ToLowerInvariant();
	}

	private static string ReadLegacyId(JsonElement element, int limit)
	{
		if (element.ValueKind != JsonValueKind.String)
		{
			throw FormatError();
		}
		string text = element.GetString();
		if (string.IsNullOrWhiteSpace(text) || text.Length > limit || text != text.Trim() || text.Any(char.IsControl))
		{
			throw FormatError();
		}
		StrictUtf8.GetByteCount(text);
		return text;
	}

	private static string ReadFingerprint(JsonElement element)
	{
		if (element.ValueKind != JsonValueKind.String)
		{
			throw FormatError();
		}
		string text = element.GetString();
		if (text.Length != 64 || text.Any(delegate(char ch)
		{
			bool flag;
			switch (ch)
			{
			case '0':
			case '1':
			case '2':
			case '3':
			case '4':
			case '5':
			case '6':
			case '7':
			case '8':
			case '9':
			case 'a':
			case 'b':
			case 'c':
			case 'd':
			case 'e':
			case 'f':
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			return !flag;
		}))
		{
			throw FormatError();
		}
		return text;
	}

	private static long ReadRevision(JsonElement root)
	{
		JsonElement property = root.GetProperty("revision");
		if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt64(out var value) || value < 0)
		{
			throw FormatError();
		}
		return value;
	}

	private static IReadOnlyList<ConversationEvent> ReadEvents(JsonElement items)
	{
		if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() % 2 != 0)
		{
			throw FormatError();
		}
		List<ConversationEvent> list = new List<ConversationEvent>(items.GetArrayLength());
		using JsonElement.ArrayEnumerator arrayEnumerator = items.EnumerateArray().GetEnumerator();
		ConversationEventType type;
		string text;
		DateTimeOffset result;
		for (; arrayEnumerator.MoveNext(); list.Add(new ConversationEvent(type, text, result)))
		{
			JsonElement current = arrayEnumerator.Current;
			RequireProperties(current, "event_type", "content", "occurred_at_utc");
			type = ((list.Count % 2 != 0) ? ConversationEventType.LlmReply : ConversationEventType.UserInput);
			JsonElement property = current.GetProperty("event_type");
			JsonElement property2 = current.GetProperty("content");
			JsonElement property3 = current.GetProperty("occurred_at_utc");
			if (property.ValueKind != JsonValueKind.String || property2.ValueKind != JsonValueKind.String || property3.ValueKind != JsonValueKind.String || property.GetString() != EventName(type))
			{
				throw FormatError();
			}
			text = property2.GetString();
			ValidateTurnText(text);
			string text2 = property3.GetString();
			if (text2 != null && DateTimeOffset.TryParseExact(text2, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out result) && !(result.Offset != TimeSpan.Zero) && !(result < EarliestEvent) && !(result > DateTimeOffset.UtcNow + MaximumFutureClockSkew) && string.Equals(result.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), text2, StringComparison.Ordinal))
			{
				if (list.Count == 0)
				{
					continue;
				}
				if (!(result < list[list.Count - 1].OccurredAtUtc))
				{
					continue;
				}
			}
			throw FormatError();
		}
		return list;
	}

	private static void ValidateAppendCapacity(ActiveConversationSnapshot current, long expectedRevision, string user)
	{
		if (current.Conversation.Revision != expectedRevision)
		{
			throw new LlmException("conversation_changed", "会话已更新，请重新载入后发送。");
		}
		if (current.Conversation.Turns.Count >= 30 || current.ShortTermMemories.Count >= 20)
		{
			throw MemoryPendingError();
		}
	}

	private static IReadOnlyList<DreamMemoryEntry> ReadMemories(JsonElement items, int characterLimit, HashSet<string> ids)
	{
		if (items.ValueKind != JsonValueKind.Array)
		{
			throw FormatError();
		}
		List<DreamMemoryEntry> list = new List<DreamMemoryEntry>(items.GetArrayLength());
		foreach (JsonElement item in items.EnumerateArray())
		{
			RequireProperties(item, "id", "content", "created_at_utc", "source_start_at_utc", "source_end_at_utc", "source_turn_count");
			JsonElement property = item.GetProperty("id");
			JsonElement property2 = item.GetProperty("content");
			JsonElement property3 = item.GetProperty("source_turn_count");
			if (property.ValueKind != JsonValueKind.String || property2.ValueKind != JsonValueKind.String || property3.ValueKind != JsonValueKind.Number || !property3.TryGetInt64(out var value) || value <= 0)
			{
				throw FormatError();
			}
			string text = property.GetString();
			ValidateSessionId(text);
			if (!ids.Add(text))
			{
				throw FormatError();
			}
			string text2 = property2.GetString();
			DreamMemoryPolicy.ValidateSummary(text2, characterLimit);
			DateTimeOffset createdAtUtc = ReadTimestamp(item.GetProperty("created_at_utc"));
			DateTimeOffset dateTimeOffset = ReadTimestamp(item.GetProperty("source_start_at_utc"));
			DateTimeOffset dateTimeOffset2 = ReadTimestamp(item.GetProperty("source_end_at_utc"));
			if (dateTimeOffset > dateTimeOffset2)
			{
				throw FormatError();
			}
			list.Add(new DreamMemoryEntry(text, text2, createdAtUtc, dateTimeOffset, dateTimeOffset2, value));
		}
		return list;
	}

	private static DateTimeOffset ReadTimestamp(JsonElement element)
	{
		if (element.ValueKind != JsonValueKind.String)
		{
			throw FormatError();
		}
		string text = element.GetString();
		if (text == null || !DateTimeOffset.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result) || result.Offset != TimeSpan.Zero || result < EarliestEvent || result > DateTimeOffset.UtcNow + MaximumFutureClockSkew || !string.Equals(result.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), text, StringComparison.Ordinal))
		{
			throw FormatError();
		}
		return result;
	}

	private static void ValidateMemoryTimeline(ActiveConversationSnapshot snapshot)
	{
		DateTimeOffset? dateTimeOffset = null;
		foreach (DreamMemoryEntry item in snapshot.LongTermMemories.Concat(snapshot.ShortTermMemories))
		{
			if (dateTimeOffset.HasValue && item.SourceStartAtUtc < dateTimeOffset.Value)
			{
				throw FormatError();
			}
			dateTimeOffset = item.SourceEndAtUtc;
		}
		if (dateTimeOffset.HasValue && snapshot.Conversation.Events.Count > 0 && snapshot.Conversation.Events[0].OccurredAtUtc < dateTimeOffset.Value)
		{
			throw FormatError();
		}
	}

	private void WriteSession(ActiveConversationSnapshot session)
	{
		byte[] data = JsonSerializer.SerializeToUtf8Bytes(new SessionFile
		{
			SchemaVersion = 3,
			SessionId = session.SessionId,
			DisplayName = session.DisplayName,
			Revision = session.Conversation.Revision,
			Events = session.Conversation.Events.Select((ConversationEvent item) => new EventFile
			{
				EventType = EventName(item.Type),
				Content = item.Content,
				OccurredAtUtc = item.OccurredAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
			}).ToArray(),
			ShortTermMemories = session.ShortTermMemories.Select(ToMemoryFile).ToArray(),
			LongTermMemories = session.LongTermMemories.Select(ToMemoryFile).ToArray()
		}, JsonOptions);
		WriteAtomic(SessionPath(session.SessionId), data);
	}

	private static MemoryFile ToMemoryFile(DreamMemoryEntry memory)
	{
		return new MemoryFile
		{
			Id = memory.Id,
			Content = memory.Content,
			CreatedAtUtc = memory.CreatedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
			SourceStartAtUtc = memory.SourceStartAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
			SourceEndAtUtc = memory.SourceEndAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
			SourceTurnCount = memory.SourceTurnCount
		};
	}

	private static void WriteAtomic(string path, byte[] data, int? limit = null)
	{
		if (limit.HasValue && data.Length > limit.Value)
		{
			throw new LlmException("conversation_limit", "会话已达到文件大小上限；原会话保持不变。");
		}
		string text = path + ".pending-" + Guid.NewGuid().ToString("N");
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			using (FileStream fileStream = new FileStream(text, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.WriteThrough))
			{
				fileStream.Write(data, 0, data.Length);
				fileStream.Flush(flushToDisk: true);
			}
			if (File.Exists(path))
			{
				File.Replace(text, path, null);
			}
			else
			{
				File.Move(text, path);
			}
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			throw new LlmException("conversation_write", "保存会话失败；原会话保持不变。");
		}
		finally
		{
			try
			{
				if (File.Exists(text))
				{
					File.Delete(text);
				}
			}
			catch (Exception ex2) when (((ex2 is IOException || ex2 is UnauthorizedAccessException) ? 1 : 0) != 0)
			{
			}
		}
	}

	private static JsonDocument ParseFile(string path, int? limit = null)
	{
		using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.SequentialScan);
		if (limit.HasValue && fileStream.Length > limit.Value)
		{
			throw new LlmException("conversation_limit", "工作区文件超过大小限制；文件保持不变。");
		}
		using MemoryStream memoryStream = new MemoryStream();
		byte[] array = new byte[8192];
		int num;
		while ((num = fileStream.Read(array, 0, array.Length)) != 0)
		{
			if (limit.HasValue && memoryStream.Length + num > limit.Value)
			{
				throw new LlmException("conversation_limit", "工作区文件超过大小限制；文件保持不变。");
			}
			memoryStream.Write(array, 0, num);
		}
		return JsonDocument.Parse(StrictUtf8.GetString(memoryStream.ToArray()), new JsonDocumentOptions
		{
			MaxDepth = 16
		});
	}

	private static void RequireProperties(JsonElement element, params string[] expected)
	{
		if (element.ValueKind != JsonValueKind.Object)
		{
			throw FormatError();
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		foreach (JsonProperty item in element.EnumerateObject())
		{
			if (!expected.Contains<string>(item.Name, StringComparer.Ordinal) || !hashSet.Add(item.Name))
			{
				throw FormatError();
			}
		}
		if (hashSet.Count != expected.Length)
		{
			throw FormatError();
		}
	}

	private static void RequireSessionProperties(JsonElement element, int version)
	{
		if (version == 3)
		{
			RequireProperties(element, "schema_version", "session_id", "display_name", "revision", "events", "short_term_memories", "long_term_memories");
			return;
		}
		if (element.ValueKind != JsonValueKind.Object)
		{
			throw FormatError();
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		foreach (JsonProperty item in element.EnumerateObject())
		{
			bool flag;
			switch (item.Name)
			{
			case "schema_version":
			case "session_id":
			case "display_name":
			case "revision":
			case "events":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (!flag || !hashSet.Add(item.Name))
			{
				throw FormatError();
			}
		}
		if (hashSet.IsSupersetOf(new string[4] { "schema_version", "session_id", "revision", "events" }))
		{
			return;
		}
		throw FormatError();
	}

	private static string NormalizeDisplayName(string? value)
	{
		string text = value?.Trim();
		if (string.IsNullOrWhiteSpace(text) || text.Length > 64 || text.Any(char.IsControl))
		{
			throw new LlmException("conversation_name", "幻境名称为空、过长或包含无效字符；原名称保持不变。");
		}
		try
		{
			StrictUtf8.GetByteCount(text);
			return text;
		}
		catch (EncoderFallbackException)
		{
			throw new LlmException("conversation_name", "幻境名称编码不正确；原名称保持不变。");
		}
	}

	private static string ReadDisplayName(JsonElement element)
	{
		if (element.ValueKind != JsonValueKind.String)
		{
			throw FormatError();
		}
		string text = element.GetString();
		try
		{
			if (!string.Equals(text, NormalizeDisplayName(text), StringComparison.Ordinal))
			{
				throw FormatError();
			}
			return text;
		}
		catch (LlmException ex) when (ex.Code == "conversation_name")
		{
			throw FormatError();
		}
	}

	private string[] SessionPaths()
	{
		try
		{
			if (!Directory.Exists(sessionsDirectory))
			{
				return Array.Empty<string>();
			}
			string[] array = (from path in Directory.EnumerateFiles(sessionsDirectory, "session-*.json", SearchOption.TopDirectoryOnly)
				where Path.GetFileName(path).StartsWith("session-", StringComparison.Ordinal) && Path.GetFileName(path).EndsWith(".json", StringComparison.Ordinal)
				select path).ToArray();
			if (array.Length > 10000)
			{
				throw new LlmException("conversation_limit", "会话文件数量超过列表上限；原文件保持不变。");
			}
			return array;
		}
		catch (LlmException)
		{
			throw;
		}
		catch (Exception ex2) when (((ex2 is IOException || ex2 is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			throw new LlmException("conversation_read", "列出会话失败；原文件保持不变。");
		}
	}

	private static Dictionary<string, string> FallbackNames(string[] paths)
	{
		return paths.OrderBy(File.GetCreationTimeUtc).ThenBy<string, string>(Path.GetFileName, StringComparer.Ordinal).Select((string path, int index) => (path: path, name: "新建幻境" + (index + 1).ToString(CultureInfo.InvariantCulture)))
			.ToDictionary<(string, string), string, string>(((string path, string name) item) => item.path, ((string path, string name) item) => item.name, StringComparer.OrdinalIgnoreCase);
	}

	private string FallbackName(string id)
	{
		if (!FallbackNames(SessionPaths()).TryGetValue(SessionPath(id), out string value))
		{
			throw FormatError();
		}
		return value;
	}

	private string NextDefaultName()
	{
		string[] array = SessionPaths();
		if (array.Length >= 10000)
		{
			throw new LlmException("conversation_limit", "会话数量达到上限；原会话保持不变。");
		}
		Dictionary<string, string> dictionary = FallbackNames(array);
		ulong num = (ulong)array.Length + 1uL;
		string[] array2 = array;
		bool flag = default(bool);
		foreach (string text in array2)
		{
			string fileName = Path.GetFileName(text);
			string id = fileName.Substring("session-".Length, fileName.Length - "session-".Length - ".json".Length);
			string displayName;
			try
			{
				ValidateSessionId(id);
				displayName = ReadSession(id, dictionary[text]).DisplayName;
			}
			catch (LlmException ex) when (((Func<bool>)delegate
			{
				// Could not convert BlockContainer to single expression
				switch (ex.Code)
				{
				case "conversation_id":
				case "conversation_format":
				case "conversation_version":
				case "conversation_read":
				case "conversation_limit":
				case "conversation_name":
					flag = true;
					break;
				default:
					flag = false;
					break;
				}
				return flag;
			}).Invoke())
			{
				continue;
			}
			if (displayName.StartsWith("新建幻境", StringComparison.Ordinal) && ulong.TryParse(displayName.Substring("新建幻境".Length), NumberStyles.None, CultureInfo.InvariantCulture, out var result) && result >= num)
			{
				if (result == ulong.MaxValue)
				{
					throw new LlmException("conversation_limit", "幻境默认名称序号已达到上限；原会话保持不变。");
				}
				num = result + 1;
			}
		}
		return "新建幻境" + num.ToString(CultureInfo.InvariantCulture);
	}

	private static void ValidateSessionId(string? id)
	{
		if (id == null || id.Length != 32 || id.Any(delegate(char ch)
		{
			bool flag;
			switch (ch)
			{
			case '0':
			case '1':
			case '2':
			case '3':
			case '4':
			case '5':
			case '6':
			case '7':
			case '8':
			case '9':
			case 'a':
			case 'b':
			case 'c':
			case 'd':
			case 'e':
			case 'f':
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			return !flag;
		}))
		{
			throw new LlmException("conversation_id", "活动会话标识不正确。");
		}
	}

	private static void ValidateTurnText(string? value)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Length > 65536 || value.Any(delegate(char ch)
		{
			bool flag = char.IsControl(ch);
			if (flag)
			{
				bool flag2;
				switch (ch)
				{
				case '\t':
				case '\n':
				case '\r':
					flag2 = true;
					break;
				default:
					flag2 = false;
					break;
				}
				flag = !flag2;
			}
			return flag;
		}))
		{
			throw new LlmException("conversation_text", "对话内容为空、过长或包含无效字符；原会话保持不变。");
		}
		try
		{
			StrictUtf8.GetByteCount(value);
		}
		catch (EncoderFallbackException)
		{
			throw new LlmException("conversation_text", "对话内容编码不正确；原会话保持不变。");
		}
	}

	private static void ValidateTimes(DateTimeOffset userAt, DateTimeOffset assistantAt)
	{
		if (userAt.Offset != TimeSpan.Zero || assistantAt.Offset != TimeSpan.Zero || userAt < EarliestEvent || userAt > assistantAt || assistantAt > DateTimeOffset.UtcNow + MaximumFutureClockSkew)
		{
			throw new LlmException("conversation_time", "会话时间戳必须为有序的 UTC 时间；原会话保持不变。");
		}
	}

	private static ActiveConversationSnapshot Snapshot(string id, long revision, IReadOnlyList<ConversationEvent> events, string displayName)
	{
		return new ActiveConversationSnapshot(id, new ConversationSnapshot("active", "active", revision, events))
		{
			DisplayName = displayName
		};
	}

	private string SessionPath(string id)
	{
		return Path.Combine(sessionsDirectory, "session-" + id + ".json");
	}

	private static string EventName(ConversationEventType type)
	{
		if (type != ConversationEventType.UserInput)
		{
			return "llm_reply";
		}
		return "user_input";
	}

	private static LlmException FormatError()
	{
		return new LlmException("conversation_format", "会话文件格式异常；原文件保持不变。");
	}

	private static LlmException RevisionError()
	{
		return new LlmException("conversation_revision", "会话修订号不正确；原会话保持不变。");
	}

	private static LlmException MemoryPendingError()
	{
		return new LlmException("memory_pending", "幻境记忆正在等待整理；原始记录已保留，请完成整理后继续发送。");
	}
}
