using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Just_Lilith.Core.Contracts;

namespace Just_Lilith.Core.Speech;

public sealed class SpeechSettingsStore
{
	private readonly string path;

	private readonly object gate;

	private static readonly ConcurrentDictionary<string, object> Gates = new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);

	private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
	{
		WriteIndented = true,
		MaxDepth = 8,
		Converters = { (JsonConverter)new JsonStringEnumConverter(null, allowIntegerValues: false) }
	};

	public SpeechSettingsStore(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new SpeechException("settings_path", "TTS 配置路径为空。");
		}
		try
		{
			this.path = Path.GetFullPath(path);
		}
		catch (Exception ex) when (((ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) ? 1 : 0) != 0)
		{
			throw new SpeechException("settings_path", "TTS 配置路径格式不正确。");
		}
		gate = Gates.GetOrAdd(this.path, (string _) => new object());
	}

	public SpeechSettings Load()
	{
		lock (gate)
		{
			return Read();
		}
	}

	public SpeechSettings Save(SpeechSettings proposed)
	{
		lock (gate)
		{
			Read();
			SpeechSettings speechSettings = Validate(proposed);
			string sourceFileName = path + ".pending-" + Guid.NewGuid().ToString("N");
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				byte[] array = JsonSerializer.SerializeToUtf8Bytes(speechSettings, Options);
				using (FileStream fileStream = new FileStream(sourceFileName, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
				{
					fileStream.Write(array, 0, array.Length);
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
				return speechSettings;
			}
			catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
			{
				throw new SpeechException("settings_write", "TTS 配置保存失败；原配置保持不变。");
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
	}

	private SpeechSettings Read()
	{
		try
		{
			if (!File.Exists(path))
			{
				return new SpeechSettings();
			}
			if (new FileInfo(path).Length > 8192)
			{
				throw new SpeechException("settings_format", "TTS 配置文件超过大小限制；文件保持不变。");
			}
			byte[] array = File.ReadAllBytes(path);
			using JsonDocument jsonDocument = JsonDocument.Parse(array, new JsonDocumentOptions
			{
				MaxDepth = 8
			});
			JsonElement rootElement = jsonDocument.RootElement;
			if (rootElement.ValueKind != JsonValueKind.Object)
			{
				throw new SpeechException("settings_version", "TTS 配置版本不受支持；文件保持不变。");
			}
			HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
			foreach (JsonProperty item in rootElement.EnumerateObject())
			{
				if (!hashSet.Add(item.Name))
				{
					throw new SpeechException("settings_format", "TTS 配置包含重复字段；文件保持不变。");
				}
			}
			int value2 = default(int);
			bool flag = !rootElement.TryGetProperty("schema_version", out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out value2);
			if (!flag)
			{
				bool flag2 = (uint)(value2 - 1) <= 2u;
				flag = !flag2;
			}
			if (flag)
			{
				throw new SpeechException("settings_version", "TTS 配置版本不受支持；文件保持不变。");
			}
			HashSet<string> hashSet2 = new HashSet<string>(new string[6] { "schema_version", "enabled", "language", "reactions_enabled", "volume", "service_url" }, StringComparer.Ordinal);
			if (value2 >= 2)
			{
				hashSet2.Add("reference_mode");
				hashSet2.Add("forced_reference_style");
			}
			if (value2 == 3)
			{
				hashSet2.Add("service_enabled");
			}
			if (!hashSet.SetEquals(hashSet2))
			{
				throw new SpeechException("settings_format", "TTS 配置字段不完整或包含未知字段；文件保持不变。");
			}
			string[] array2 = ((value2 != 3) ? new string[2] { "enabled", "reactions_enabled" } : new string[3] { "enabled", "reactions_enabled", "service_enabled" });
			foreach (string propertyName in array2)
			{
				JsonValueKind valueKind = rootElement.GetProperty(propertyName).ValueKind;
				if (valueKind - 5 > JsonValueKind.Object)
				{
					throw new SpeechException("settings_format", "TTS 开关字段须为布尔值；文件保持不变。");
				}
			}
			JsonElement property = rootElement.GetProperty("language");
			flag = property.ValueKind != JsonValueKind.String;
			if (!flag)
			{
				string text = property.GetString();
				bool flag2 = ((text == "Chinese" || text == "Japanese") ? true : false);
				flag = !flag2;
			}
			if (flag || rootElement.GetProperty("volume").ValueKind != JsonValueKind.Number || rootElement.GetProperty("service_url").ValueKind != JsonValueKind.String)
			{
				throw new SpeechException("settings_format", "TTS 语言、音量或服务地址字段格式不正确；文件保持不变。");
			}
			if (value2 >= 2)
			{
				JsonElement property2 = rootElement.GetProperty("reference_mode");
				JsonElement property3 = rootElement.GetProperty("forced_reference_style");
				flag = property2.ValueKind != JsonValueKind.String;
				if (!flag)
				{
					string text = property2.GetString();
					bool flag2 = ((text == "Automatic" || text == "Manual") ? true : false);
					flag = !flag2;
				}
				if (flag || property3.ValueKind != JsonValueKind.String || !SpeechReferenceStyles.IsValid(SpeechReferenceStyles.Canonicalize(property3.GetString())))
				{
					throw new SpeechException("settings_format", "TTS 参考模式或强制参考类型不正确；文件保持不变。");
				}
			}
			return Validate(JsonSerializer.Deserialize<SpeechSettings>(array, Options));
		}
		catch (JsonException)
		{
			throw new SpeechException("settings_format", "TTS 配置 JSON 格式不正确；文件保持不变。");
		}
		catch (Exception ex2) when (((ex2 is IOException || ex2 is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			throw new SpeechException("settings_read", "TTS 配置读取失败；文件保持不变。");
		}
	}

	private static SpeechSettings Validate(SpeechSettings? value)
	{
		bool flag = (object)value == null;
		if (!flag)
		{
			int schemaVersion = value.SchemaVersion;
			bool flag2 = (uint)(schemaVersion - 1) <= 2u;
			flag = !flag2;
		}
		if (flag)
		{
			throw new SpeechException("settings_version", "TTS 配置版本不受支持；原配置保持不变。");
		}
		flag = !Enum.IsDefined(typeof(SpeechLanguage), value.Language) || !Enum.IsDefined(typeof(SpeechReferenceMode), value.ReferenceMode) || !SpeechReferenceStyles.IsValid(SpeechReferenceStyles.Canonicalize(value.ForcedReferenceStyle)) || !float.IsFinite(value.Volume);
		if (!flag)
		{
			float volume = value.Volume;
			bool flag2 = ((volume < 0f || volume > 1f) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			throw new SpeechException("settings_format", "TTS 语言、参考偏好或音量值不正确；原配置保持不变。");
		}
		return value with
		{
			SchemaVersion = 3,
			ForcedReferenceStyle = SpeechReferenceStyles.Canonicalize(value.ForcedReferenceStyle),
			ServiceEnabled = (value.SchemaVersion == 3 && value.ServiceEnabled),
			ServiceUrl = SpeechEndpoint.Normalize(value.ServiceUrl)
		};
	}
}
