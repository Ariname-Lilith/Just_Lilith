using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Just_Lilith.Core.Llm;

public sealed class LlmSettingsStore
{
	private sealed class LegacySettings
	{
		[JsonPropertyName("revision")]
		public long Revision { get; init; }

		[JsonPropertyName("base_url")]
		public string? BaseUrl { get; init; }

		[JsonPropertyName("protected_api_key")]
		public string? ProtectedApiKey { get; init; }

		[JsonPropertyName("model_id")]
		public string? ModelId { get; init; }

		[JsonPropertyName("api_format")]
		public LlmApiFormat ApiFormat { get; init; }

		[JsonPropertyName("reasoning_mode")]
		public ReasoningMode ReasoningMode { get; init; }

		[JsonPropertyName("custom_reasoning_effort")]
		public string? CustomReasoningEffort { get; init; } = "low";
	}

	private readonly string path;

	private readonly ISecretProtector protector;

	private readonly object gate;

	private static readonly ConcurrentDictionary<string, object> Gates = new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true,
		MaxDepth = 16,
		Converters = { (JsonConverter)new JsonStringEnumConverter(null, allowIntegerValues: false) }
	};

	public LlmSettingsStore(string path, ISecretProtector? protector = null)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new LlmException("settings_path", "配置文件路径不正确。");
		}
		try
		{
			this.path = Path.GetFullPath(path);
		}
		catch (Exception ex) when (((ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) ? 1 : 0) != 0)
		{
			throw new LlmException("settings_path", "配置文件路径不正确。");
		}
		this.protector = protector ?? new WindowsSecretProtector();
		gate = Gates.GetOrAdd(this.path, (string _) => new object());
	}

	public LlmSettings Load()
	{
		lock (gate)
		{
			return Read();
		}
	}

	public LlmSettings Save(LlmSettings proposed, IReadOnlyDictionary<string, ApiKeyUpdate>? keyUpdates = null)
	{
		if ((object)proposed == null)
		{
			throw new LlmException("settings_format", "待保存的配置为空；原配置保持不变。");
		}
		lock (gate)
		{
			LlmSettings current = Read();
			if (proposed.SchemaVersion != 2)
			{
				throw new LlmException("settings_version", "配置版本不受支持；原配置保持不变。");
			}
			if (proposed.Revision != current.Revision)
			{
				throw new LlmException("settings_changed", "配置已被更新，请重新载入后保存。");
			}
			ValidateProfileSet(proposed.Profiles, current.Profiles);
			if (keyUpdates != null && keyUpdates.Keys.Any((string id) => string.IsNullOrWhiteSpace(id) || current.Profiles.All((LlmProfileSettings p) => p.ProfileId != id)))
			{
				throw new LlmException("profile_id", "API Key 更新指向了未知配置。");
			}
			Dictionary<string, LlmProfileSettings> dictionary = current.Profiles.ToDictionary<LlmProfileSettings, string>((LlmProfileSettings p) => p.ProfileId, StringComparer.Ordinal);
			LlmProfileSettings[] array = new LlmProfileSettings[3];
			for (int num = 0; num < proposed.Profiles.Length; num++)
			{
				LlmProfileSettings llmProfileSettings = proposed.Profiles[num];
				LlmProfileSettings llmProfileSettings2 = dictionary[llmProfileSettings.ProfileId];
				ApiKeyUpdate apiKeyUpdate = ((keyUpdates != null && keyUpdates.TryGetValue(llmProfileSettings.ProfileId, out ApiKeyUpdate value)) ? value : ApiKeyUpdate.Keep);
				string text = NormalizeOptionalUrl(llmProfileSettings.BaseUrl);
				string b = NormalizeOptionalUrl(llmProfileSettings2.BaseUrl);
				string protectedApiKey = ApplyKeyUpdate(apiKeyUpdate, llmProfileSettings2.ProtectedApiKey);
				if (apiKeyUpdate.Mode == ApiKeyUpdateMode.Keep && llmProfileSettings2.HasApiKey && !string.Equals(text, b, StringComparison.Ordinal))
				{
					throw new LlmException("key_required_for_url", "更换 API 地址时，请重新输入该配置的 API Key，以免将旧密钥发送到其他服务。");
				}
				array[num] = llmProfileSettings with
				{
					ProfileId = llmProfileSettings.ProfileId.Trim(),
					DisplayName = (llmProfileSettings.DisplayName?.Trim() ?? ""),
					BaseUrl = text,
					ProtectedApiKey = protectedApiKey,
					ModelId = (llmProfileSettings.ModelId?.Trim() ?? ""),
					CustomReasoningEffort = (llmProfileSettings.CustomReasoningEffort?.Trim().ToLowerInvariant() ?? "low")
				};
			}
			long revision;
			try
			{
				revision = checked(current.Revision + 1);
			}
			catch (OverflowException)
			{
				throw new LlmException("settings_revision", "配置修订号已达上限；原配置保持不变。");
			}
			LlmSettings llmSettings = proposed with
			{
				SchemaVersion = 2,
				Revision = revision,
				Profiles = array
			};
			Validate(llmSettings);
			WriteAtomic(llmSettings);
			return Clone(llmSettings);
		}
	}

	private string ApplyKeyUpdate(ApiKeyUpdate? update, string existing)
	{
		if ((object)update == null)
		{
			throw new LlmException("invalid_key", "API Key 更新方式不正确。");
		}
		if (!Enum.IsDefined(typeof(ApiKeyUpdateMode), update.Mode))
		{
			throw new LlmException("invalid_key", "API Key 更新方式不正确。");
		}
		if (update.Mode == ApiKeyUpdateMode.Keep)
		{
			return existing;
		}
		if (update.Mode == ApiKeyUpdateMode.Clear)
		{
			return "";
		}
		string text = update.Value?.Trim() ?? "";
		if (text.Length == 0 || text.Length > 8192 || text.Any(char.IsControl))
		{
			throw new LlmException("invalid_key", "API Key 格式不正确，请重新输入。");
		}
		try
		{
			return protector.Protect(text);
		}
		catch (LlmException)
		{
			throw;
		}
		catch (Exception)
		{
			throw new LlmException("key_storage", "API Key 加密存储失败，请重新输入并保存。");
		}
	}

	private void WriteAtomic(LlmSettings saved)
	{
		string sourceFileName = path + ".pending-" + Guid.NewGuid().ToString("N");
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(saved, JsonOptions));
			using (FileStream fileStream = new FileStream(sourceFileName, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
			{
				fileStream.Write(bytes, 0, bytes.Length);
				fileStream.Flush(flushToDisk: true);
			}
			if (File.Exists(path))
			{
				File.Replace(sourceFileName, path, null);
			}
			else
			{
				File.Move(sourceFileName, path);
			}
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			throw new LlmException("settings_write", "保存配置失败，请检查配置目录的写入权限；原配置保持不变。");
		}
		finally
		{
			try
			{
				if (File.Exists(sourceFileName))
				{
					File.Delete(sourceFileName);
				}
			}
			catch (Exception ex2) when (((ex2 is IOException || ex2 is UnauthorizedAccessException) ? 1 : 0) != 0)
			{
			}
		}
	}

	private LlmSettings Read()
	{
		try
		{
			if (!File.Exists(path))
			{
				return LlmSettings.CreateDefault();
			}
			if (new FileInfo(path).Length > 65536)
			{
				throw new LlmException("settings_format", "配置文件超过大小限制，请检查配置文件。");
			}
			string json = File.ReadAllText(path);
			using JsonDocument jsonDocument = JsonDocument.Parse(json, new JsonDocumentOptions
			{
				MaxDepth = 16
			});
			if (jsonDocument.RootElement.ValueKind != JsonValueKind.Object)
			{
				throw new LlmException("settings_format", "配置文件格式异常；文件未被替换。");
			}
			int value2 = default(int);
			bool flag = !jsonDocument.RootElement.TryGetProperty("schema_version", out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out value2);
			if (!flag)
			{
				bool flag2 = (uint)(value2 - 1) <= 1u;
				flag = !flag2;
			}
			if (flag)
			{
				throw new LlmException("settings_version", "配置版本不受支持；文件未被替换。");
			}
			LlmSettings llmSettings = ((value2 != 1) ? (JsonSerializer.Deserialize<LlmSettings>(json, JsonOptions) ?? throw new LlmException("settings_format", "配置文件为空，请检查配置文件。")) : Migrate(JsonSerializer.Deserialize<LegacySettings>(json, JsonOptions) ?? throw new LlmException("settings_format", "配置文件为空，请检查配置文件。")));
			Validate(llmSettings);
			return Clone(llmSettings);
		}
		catch (JsonException)
		{
			throw new LlmException("settings_format", "配置文件格式异常；文件未被替换。");
		}
		catch (LlmException)
		{
			throw;
		}
		catch (Exception ex3) when (((ex3 is IOException || ex3 is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			throw new LlmException("settings_read", "读取配置失败，请检查配置文件和读取权限。");
		}
	}

	private static LlmSettings Migrate(LegacySettings legacy)
	{
		LlmProfileSettings[] array = LlmSettings.CreateDefaultProfiles();
		array[0] = array[0]with
		{
			BaseUrl = (legacy.BaseUrl ?? ""),
			ProtectedApiKey = (legacy.ProtectedApiKey ?? ""),
			ModelId = (legacy.ModelId ?? ""),
			ApiFormat = legacy.ApiFormat,
			ReasoningMode = legacy.ReasoningMode,
			CustomReasoningEffort = (legacy.CustomReasoningEffort ?? "low")
		};
		return new LlmSettings
		{
			Revision = legacy.Revision,
			Profiles = array
		};
	}

	private static string NormalizeOptionalUrl(string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return LlmEndpoint.Normalize(value);
		}
		return "";
	}

	private static void ValidateProfileSet(LlmProfileSettings[]? proposed, LlmProfileSettings[]? current)
	{
		if (proposed == null || current == null || proposed.Length != 3 || current.Length != 3 || proposed.Any((LlmProfileSettings profile) => (object)profile == null) || current.Any((LlmProfileSettings profile) => (object)profile == null))
		{
			throw new LlmException("profile_count", "必须保留三套 API 配置。");
		}
		if (proposed.Any((LlmProfileSettings profile) => string.IsNullOrWhiteSpace(profile.ProfileId)) || current.Any((LlmProfileSettings profile) => string.IsNullOrWhiteSpace(profile.ProfileId)))
		{
			throw new LlmException("profile_id", "配置标识发生变化，请重新载入。");
		}
		string[] first = current.Select((LlmProfileSettings p) => p.ProfileId).OrderBy<string, string>((string x) => x, StringComparer.Ordinal).ToArray();
		string[] second = proposed.Select((LlmProfileSettings p) => p.ProfileId).OrderBy<string, string>((string x) => x, StringComparer.Ordinal).ToArray();
		if (!Enumerable.SequenceEqual<string>(first, second, StringComparer.Ordinal))
		{
			throw new LlmException("profile_id", "配置标识发生变化，请重新载入。");
		}
	}

	private static void Validate(LlmSettings? settings)
	{
		if ((object)settings == null)
		{
			throw new LlmException("settings_format", "配置文件为空，请检查配置文件。");
		}
		if (settings.SchemaVersion != 2 || settings.Revision < 0)
		{
			throw new LlmException("settings_version", "配置版本不受支持；文件未被替换。");
		}
		if (settings.Profiles == null || settings.Profiles.Length != 3)
		{
			throw new LlmException("profile_count", "配置文件应包含三套 API 配置。");
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		LlmProfileSettings[] profiles = settings.Profiles;
		foreach (LlmProfileSettings llmProfileSettings in profiles)
		{
			if ((object)llmProfileSettings == null || string.IsNullOrWhiteSpace(llmProfileSettings.ProfileId) || llmProfileSettings.ProfileId.Length > 64 || llmProfileSettings.ProfileId.Any(char.IsControl) || !hashSet.Add(llmProfileSettings.ProfileId) || string.IsNullOrWhiteSpace(llmProfileSettings.DisplayName) || llmProfileSettings.DisplayName.Length > 32 || llmProfileSettings.DisplayName.Any(char.IsControl) || llmProfileSettings.ProtectedApiKey == null || llmProfileSettings.ProtectedApiKey.Length > 32768 || llmProfileSettings.ModelId == null || llmProfileSettings.ModelId.Length > 256 || llmProfileSettings.ModelId.Any(char.IsControl) || llmProfileSettings.CustomReasoningEffort == null || !Enum.IsDefined(typeof(LlmApiFormat), llmProfileSettings.ApiFormat) || !Enum.IsDefined(typeof(ReasoningMode), llmProfileSettings.ReasoningMode))
			{
				throw new LlmException("settings_format", "配置字段不正确，请检查配置文件。");
			}
			if (NormalizeOptionalUrl(llmProfileSettings.BaseUrl).Length == 0 && (llmProfileSettings.HasApiKey || llmProfileSettings.ModelId.Length != 0))
			{
				throw new LlmException("invalid_url", "含密钥或模型的配置必须填写 API 地址。");
			}
			if (!ReasoningPolicy.CustomEfforts.Contains<string>(llmProfileSettings.CustomReasoningEffort, StringComparer.Ordinal))
			{
				throw new LlmException("reasoning_effort", "请选择支持的推理强度值。");
			}
			if (llmProfileSettings.ModelId.Length != 0)
			{
				ReasoningPolicy.Evaluate(llmProfileSettings.ModelId, llmProfileSettings.ReasoningMode, llmProfileSettings.CustomReasoningEffort);
			}
		}
	}

	private static LlmSettings Clone(LlmSettings value)
	{
		return value with
		{
			Profiles = value.Profiles.Select((LlmProfileSettings profile) => profile with { }).ToArray()
		};
	}
}
