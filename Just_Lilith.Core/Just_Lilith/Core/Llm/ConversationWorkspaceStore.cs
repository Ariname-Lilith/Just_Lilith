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
using System.Threading;

namespace Just_Lilith.Core.Llm;

public sealed class ConversationWorkspaceStore
{
	private readonly record struct SessionKey(string Path, string EndpointFingerprint, string CredentialFingerprint);

	private sealed record ConversationFile
	{
		[JsonPropertyName("schema_version")]
		public int SchemaVersion { get; init; }

		[JsonPropertyName("revision")]
		public long Revision { get; init; }

		[JsonPropertyName("profile_id")]
		public string ProfileId { get; init; } = "";

		[JsonPropertyName("model_id")]
		public string ModelId { get; init; } = "";

		[JsonPropertyName("endpoint_fingerprint")]
		public string EndpointFingerprint { get; init; } = "";

		[JsonPropertyName("credential_fingerprint")]
		public string CredentialFingerprint { get; init; } = "";

		[JsonPropertyName("events")]
		public ConversationEventFile[] Events { get; init; } = Array.Empty<ConversationEventFile>();
	}

	private sealed record ConversationEventFile
	{
		[JsonPropertyName("event_type")]
		public string EventType { get; init; } = "";

		[JsonPropertyName("content")]
		public string Content { get; init; } = "";

		[JsonPropertyName("occurred_at_utc")]
		public string OccurredAtUtc { get; init; } = "";
	}

	public const int SystemPromptByteLimit = 65536;

	public const int SystemPromptCharacterLimit = 32768;

	public const int SessionByteLimit = 8388608;

	public const int TurnTextCharacterLimit = 65536;

	public const int TurnCountLimit = 10000;

	private const int CurrentSchemaVersion = 1;

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

	private readonly string promptPath;

	private readonly string legacyPromptPath;

	public string SystemPromptPathOnDisk => promptPath;

	public ConversationWorkspaceStore(string workspaceDirectory)
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
		promptPath = Path.Combine(this.workspaceDirectory, "Persona.md");
		legacyPromptPath = Path.Combine(this.workspaceDirectory, "system.md");
	}

	public string LoadSystemPrompt()
	{
		lock (GateFor(promptPath))
		{
			try
			{
				Directory.CreateDirectory(workspaceDirectory);
				MigrateLegacyPrompt();
				try
				{
					using FileStream fileStream = new FileStream(promptPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
					fileStream.Flush(flushToDisk: true);
				}
				catch (IOException) when (File.Exists(promptPath))
				{
				}
				byte[] array = ReadBounded(promptPath, 65536, "system_prompt_limit");
				int num = ((array.Length >= 3 && array[0] == 239 && array[1] == 187 && array[2] == 191) ? 3 : 0);
				string text = StrictUtf8.GetString(array, num, array.Length - num);
				if (text.Length > 32768)
				{
					throw new LlmException("system_prompt_limit", "系统提示词超过长度限制；文件保持不变。");
				}
				if (text.Any(delegate(char ch)
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
					throw new LlmException("system_prompt_format", "系统提示词包含无效控制字符；文件保持不变。");
				}
				return text;
			}
			catch (DecoderFallbackException)
			{
				throw new LlmException("system_prompt_format", "系统提示词不是有效的 UTF-8；文件保持不变。");
			}
			catch (LlmException)
			{
				throw;
			}
			catch (Exception ex4) when (((ex4 is IOException || ex4 is UnauthorizedAccessException) ? 1 : 0) != 0)
			{
				throw new LlmException("system_prompt_read", "读取系统提示词失败；文件保持不变。");
			}
		}
	}

	private void MigrateLegacyPrompt()
	{
		if (!File.Exists(legacyPromptPath))
		{
			return;
		}
		if (!File.Exists(promptPath))
		{
			File.Move(legacyPromptPath, promptPath);
			return;
		}
		if (!FilesAreIdentical(legacyPromptPath, promptPath))
		{
			throw new LlmException("system_prompt_migration_conflict", "Persona.md 与旧 system.md 内容不同；两个文件均已保留，请手动合并后重试。");
		}
		File.Delete(legacyPromptPath);
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

	public ConversationSnapshot LoadSession(string profileId, string modelId, string baseUrl, string credentialScope)
	{
		SessionKey key = SessionKeyFor(profileId, modelId, baseUrl, credentialScope);
		lock (GateFor(key.Path))
		{
			return ReadSession(key, profileId, modelId);
		}
	}

	public ConversationSnapshot EnsureCanAppend(string profileId, string modelId, string baseUrl, string credentialScope, long expectedRevision, string user)
	{
		SessionKey key = SessionKeyFor(profileId, modelId, baseUrl, credentialScope);
		ValidateTurnText(user);
		if (expectedRevision < 0)
		{
			throw new LlmException("conversation_revision", "会话修订号不正确；原会话保持不变。");
		}
		lock (GateFor(key.Path))
		{
			ConversationSnapshot conversationSnapshot = ReadSession(key, profileId, modelId);
			ValidateAppendCapacity(key.Path, conversationSnapshot, expectedRevision, user);
			return conversationSnapshot;
		}
	}

	public ConversationSnapshot AppendCompletedTurn(string profileId, string modelId, string baseUrl, string credentialScope, long expectedRevision, string user, DateTimeOffset userOccurredAtUtc, string assistant, DateTimeOffset assistantOccurredAtUtc, CancellationToken cancellationToken = default(CancellationToken))
	{
		SessionKey key = SessionKeyFor(profileId, modelId, baseUrl, credentialScope);
		ValidateTurn(user, assistant);
		ValidateTimes(userOccurredAtUtc, assistantOccurredAtUtc);
		if (expectedRevision < 0)
		{
			throw new LlmException("conversation_revision", "会话修订号不正确；原会话保持不变。");
		}
		lock (GateFor(key.Path))
		{
			cancellationToken.ThrowIfCancellationRequested();
			ConversationSnapshot conversationSnapshot = ReadSession(key, profileId, modelId);
			ValidateAppendCapacity(key.Path, conversationSnapshot, expectedRevision, user);
			if (conversationSnapshot.Events.Count != 0)
			{
				IReadOnlyList<ConversationEvent> events = conversationSnapshot.Events;
				if (events[events.Count - 1].OccurredAtUtc > userOccurredAtUtc)
				{
					throw new LlmException("conversation_time", "新消息时间早于已有会话；原会话保持不变。");
				}
			}
			long revision;
			try
			{
				revision = checked(conversationSnapshot.Revision + 1);
			}
			catch (OverflowException)
			{
				throw new LlmException("conversation_revision", "会话修订号已达到上限；原会话保持不变。");
			}
			ConversationEvent[] events2 = conversationSnapshot.Events.Concat(new ConversationEvent[2]
			{
				new ConversationEvent(ConversationEventType.UserInput, user, userOccurredAtUtc),
				new ConversationEvent(ConversationEventType.LlmReply, assistant, assistantOccurredAtUtc)
			}).ToArray();
			ConversationSnapshot conversationSnapshot2 = new ConversationSnapshot(profileId, modelId, revision, events2);
			WriteAtomic(key, conversationSnapshot2, cancellationToken);
			return conversationSnapshot2;
		}
	}

	private SessionKey SessionKeyFor(string profileId, string modelId, string baseUrl, string credentialScope)
	{
		ValidateId(profileId, 64);
		ValidateId(modelId, 256);
		ValidateCredentialScope(credentialScope);
		string s = LlmEndpoint.Normalize(baseUrl);
		string text = Convert.ToHexString(SHA256.HashData(StrictUtf8.GetBytes(s))).ToLowerInvariant();
		string text2 = Convert.ToHexString(SHA256.HashData(StrictUtf8.GetBytes(credentialScope))).ToLowerInvariant();
		byte[] bytes = StrictUtf8.GetBytes(profileId);
		byte[] bytes2 = StrictUtf8.GetBytes(modelId);
		byte[] bytes3 = StrictUtf8.GetBytes(text);
		byte[] bytes4 = StrictUtf8.GetBytes(text2);
		byte[] array = new byte[16 + bytes.Length + bytes2.Length + bytes3.Length + bytes4.Length];
		BinaryPrimitives.WriteInt32LittleEndian(array.AsSpan(0, 4), bytes.Length);
		bytes.CopyTo(array.AsSpan(4));
		int num = 4 + bytes.Length;
		BinaryPrimitives.WriteInt32LittleEndian(array.AsSpan(num, 4), bytes2.Length);
		bytes2.CopyTo(array.AsSpan(num + 4));
		int num2 = num + 4 + bytes2.Length;
		BinaryPrimitives.WriteInt32LittleEndian(array.AsSpan(num2, 4), bytes3.Length);
		bytes3.CopyTo(array.AsSpan(num2 + 4));
		int num3 = num2 + 4 + bytes3.Length;
		BinaryPrimitives.WriteInt32LittleEndian(array.AsSpan(num3, 4), bytes4.Length);
		bytes4.CopyTo(array.AsSpan(num3 + 4));
		string text3 = Convert.ToHexString(SHA256.HashData(array)).ToLowerInvariant();
		return new SessionKey(Path.Combine(workspaceDirectory, "sessions", "chat-" + text3 + ".json"), text, text2);
	}

	private static void ValidateCredentialScope(string? value)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Length > 32768 || value.Any(char.IsControl))
		{
			throw new LlmException("conversation_credential_scope", "会话凭据标识不正确。");
		}
		try
		{
			StrictUtf8.GetByteCount(value);
		}
		catch (EncoderFallbackException)
		{
			throw new LlmException("conversation_credential_scope", "会话凭据标识不正确。");
		}
	}

	private static void ValidateId(string? value, int limit)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Length > limit || !string.Equals(value, value.Trim(), StringComparison.Ordinal) || value.Any(char.IsControl))
		{
			throw new LlmException("conversation_id", "会话配置或模型标识不正确。");
		}
		try
		{
			StrictUtf8.GetByteCount(value);
		}
		catch (EncoderFallbackException)
		{
			throw new LlmException("conversation_id", "会话配置或模型标识不正确。");
		}
	}

	private static void ValidateTurn(string? user, string? assistant)
	{
		ValidateTurnText(user);
		ValidateTurnText(assistant);
	}

	private static void ValidateTimes(DateTimeOffset userOccurredAtUtc, DateTimeOffset assistantOccurredAtUtc)
	{
		if (userOccurredAtUtc.Offset != TimeSpan.Zero || assistantOccurredAtUtc.Offset != TimeSpan.Zero || userOccurredAtUtc < EarliestEvent || userOccurredAtUtc > assistantOccurredAtUtc || assistantOccurredAtUtc > DateTimeOffset.UtcNow + MaximumFutureClockSkew)
		{
			throw new LlmException("conversation_time", "会话时间戳必须为有序的 UTC 时间；原会话保持不变。");
		}
	}

	private static void ValidateAppendCapacity(string path, ConversationSnapshot current, long expectedRevision, string user)
	{
		if (current.Revision != expectedRevision)
		{
			throw new LlmException("conversation_changed", "会话已更新，请重新载入后发送。");
		}
		if (current.Turns.Count >= 10000)
		{
			throw new LlmException("conversation_limit", "会话已达到轮次上限；原会话保持不变。");
		}
		long num = ((long)user.Length + 65536L) * 6 + 4096;
		try
		{
			if ((File.Exists(path) ? new FileInfo(path).Length : 0) + num > 8388608)
			{
				throw new LlmException("conversation_limit", "会话剩余空间不足以保存完整回复；原会话保持不变。");
			}
		}
		catch (LlmException)
		{
			throw;
		}
		catch (Exception ex2) when (((ex2 is IOException || ex2 is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			throw new LlmException("conversation_read", "读取会话大小失败；原会话保持不变。");
		}
	}

	private static void ValidateTurnText(string? text)
	{
		if (string.IsNullOrWhiteSpace(text) || text.Length > 65536 || text.Any(delegate(char ch)
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
			StrictUtf8.GetByteCount(text);
		}
		catch (EncoderFallbackException)
		{
			throw new LlmException("conversation_text", "对话内容编码不正确；原会话保持不变。");
		}
	}

	private static object GateFor(string path)
	{
		return Gates.GetOrAdd(path, (string _) => new object());
	}

	private static ConversationSnapshot ReadSession(SessionKey key, string profileId, string modelId)
	{
		try
		{
			if (!File.Exists(key.Path))
			{
				return new ConversationSnapshot(profileId, modelId, 0L, Array.Empty<ConversationEvent>());
			}
			using JsonDocument jsonDocument = JsonDocument.Parse(ReadBounded(key.Path, 8388608, "conversation_limit"), new JsonDocumentOptions
			{
				MaxDepth = 16
			});
			JsonElement rootElement = jsonDocument.RootElement;
			RequireProperties(rootElement, "schema_version", "revision", "profile_id", "model_id", "endpoint_fingerprint", "credential_fingerprint", "events");
			JsonElement property = rootElement.GetProperty("schema_version");
			if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var value))
			{
				throw FormatError();
			}
			if (value != 1)
			{
				throw new LlmException("conversation_version", "会话文件版本不受支持；文件保持不变。");
			}
			JsonElement property2 = rootElement.GetProperty("revision");
			if (property2.ValueKind != JsonValueKind.Number || !property2.TryGetInt64(out var value2) || value2 < 0)
			{
				throw FormatError();
			}
			if (rootElement.GetProperty("profile_id").ValueKind != JsonValueKind.String || rootElement.GetProperty("model_id").ValueKind != JsonValueKind.String || rootElement.GetProperty("endpoint_fingerprint").ValueKind != JsonValueKind.String || rootElement.GetProperty("credential_fingerprint").ValueKind != JsonValueKind.String)
			{
				throw FormatError();
			}
			string text = rootElement.GetProperty("profile_id").GetString();
			string text2 = rootElement.GetProperty("model_id").GetString();
			ValidateId(text, 64);
			ValidateId(text2, 256);
			if (!string.Equals(text, profileId, StringComparison.Ordinal) || !string.Equals(text2, modelId, StringComparison.Ordinal) || !string.Equals(rootElement.GetProperty("endpoint_fingerprint").GetString(), key.EndpointFingerprint, StringComparison.Ordinal) || !string.Equals(rootElement.GetProperty("credential_fingerprint").GetString(), key.CredentialFingerprint, StringComparison.Ordinal))
			{
				throw FormatError();
			}
			JsonElement property3 = rootElement.GetProperty("events");
			if (property3.ValueKind != JsonValueKind.Array || property3.GetArrayLength() > 20000 || property3.GetArrayLength() % 2 != 0)
			{
				throw FormatError();
			}
			List<ConversationEvent> list = new List<ConversationEvent>(property3.GetArrayLength());
			foreach (JsonElement item in property3.EnumerateArray())
			{
				RequireProperties(item, "event_type", "content", "occurred_at_utc");
				JsonElement property4 = item.GetProperty("event_type");
				JsonElement property5 = item.GetProperty("content");
				JsonElement property6 = item.GetProperty("occurred_at_utc");
				if (property4.ValueKind != JsonValueKind.String || property5.ValueKind != JsonValueKind.String || property6.ValueKind != JsonValueKind.String)
				{
					throw FormatError();
				}
				ConversationEventType type = ((list.Count % 2 != 0) ? ConversationEventType.LlmReply : ConversationEventType.UserInput);
				if (property4.GetString() != EventTypeName(type))
				{
					throw FormatError();
				}
				string text3 = property5.GetString();
				ValidateTurnText(text3);
				string text4 = property6.GetString();
				if (text4 == null || !DateTimeOffset.TryParseExact(text4, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result) || result.Offset != TimeSpan.Zero || result < EarliestEvent || result > DateTimeOffset.UtcNow + MaximumFutureClockSkew || !string.Equals(result.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), text4, StringComparison.Ordinal))
				{
					throw FormatError();
				}
				if (list.Count != 0)
				{
					if (result < list[list.Count - 1].OccurredAtUtc)
					{
						throw FormatError();
					}
				}
				list.Add(new ConversationEvent(type, text3, result));
			}
			value2 = Math.Max(value2, list.Count / 2);
			return new ConversationSnapshot(profileId, modelId, value2, list);
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
			case "conversation_id":
			case "conversation_text":
			case "conversation_time":
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
		catch (Exception ex4) when (((ex4 is IOException || ex4 is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			throw new LlmException("conversation_read", "读取会话失败；文件保持不变。");
		}
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

	private static byte[] ReadBounded(string path, int byteLimit, string limitCode)
	{
		using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.SequentialScan);
		if (fileStream.Length > byteLimit)
		{
			throw new LlmException(limitCode, "工作区文件超过大小限制；文件保持不变。");
		}
		using MemoryStream memoryStream = new MemoryStream((int)fileStream.Length);
		byte[] array = new byte[8192];
		int num;
		while ((num = fileStream.Read(array, 0, array.Length)) != 0)
		{
			if (memoryStream.Length + num > byteLimit)
			{
				throw new LlmException(limitCode, "工作区文件超过大小限制；文件保持不变。");
			}
			memoryStream.Write(array, 0, num);
		}
		return memoryStream.ToArray();
	}

	private static void WriteAtomic(SessionKey key, ConversationSnapshot saved, CancellationToken cancellationToken)
	{
		string text = key.Path + ".pending-" + Guid.NewGuid().ToString("N");
		try
		{
			byte[] array = JsonSerializer.SerializeToUtf8Bytes(new ConversationFile
			{
				SchemaVersion = 1,
				Revision = saved.Revision,
				ProfileId = saved.ProfileId,
				ModelId = saved.ModelId,
				EndpointFingerprint = key.EndpointFingerprint,
				CredentialFingerprint = key.CredentialFingerprint,
				Events = saved.Events.Select((ConversationEvent item) => new ConversationEventFile
				{
					EventType = EventTypeName(item.Type),
					Content = item.Content,
					OccurredAtUtc = item.OccurredAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
				}).ToArray()
			}, JsonOptions);
			if (array.Length > 8388608)
			{
				throw new LlmException("conversation_limit", "会话已达到文件大小上限；原会话保持不变。");
			}
			Directory.CreateDirectory(Path.GetDirectoryName(key.Path));
			using (FileStream fileStream = new FileStream(text, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.WriteThrough))
			{
				fileStream.Write(array, 0, array.Length);
				fileStream.Flush(flushToDisk: true);
			}
			cancellationToken.ThrowIfCancellationRequested();
			if (File.Exists(key.Path))
			{
				File.Replace(text, key.Path, null);
			}
			else
			{
				File.Move(text, key.Path);
			}
		}
		catch (LlmException)
		{
			throw;
		}
		catch (Exception ex2) when (((ex2 is IOException || ex2 is UnauthorizedAccessException) ? 1 : 0) != 0)
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
			catch (Exception ex3) when (((ex3 is IOException || ex3 is UnauthorizedAccessException) ? 1 : 0) != 0)
			{
			}
		}
	}

	private static string EventTypeName(ConversationEventType type)
	{
		if (type != ConversationEventType.UserInput)
		{
			return "llm_reply";
		}
		return "user_input";
	}

	private static LlmException FormatError()
	{
		return new LlmException("conversation_format", "会话文件格式异常；文件保持不变。");
	}
}
