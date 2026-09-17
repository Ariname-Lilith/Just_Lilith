using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Just_Lilith.Core.Llm;

public sealed class WorldBookStore
{
	private sealed record LoadedWorldBook(IReadOnlyList<WorldBookEntry> Entries, string Fingerprint);

	public const string FileName = "memory.json";

	public const string LegacyFileName = "worldbook.json";

	public const int FileByteLimit = 16777216;

	public const int EntryCountLimit = 10000;

	public const int CategoryCharacterLimit = 64;

	public const int TitleCharacterLimit = 256;

	public const int ContentCharacterLimit = 4096;

	private const string DefaultResourceName = "Just_Lilith.DefaultWorldBook";

	private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

	private static readonly ConcurrentDictionary<string, object> Gates = new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);

	private readonly string path;

	private readonly string legacyPath;

	private readonly object gate;

	private string? cachedFingerprint;

	private IReadOnlyList<WorldBookEntry>? cachedEntries;

	private WorldBookRetriever? cachedRetriever;

	public string PathOnDisk => path;

	public WorldBookStore(string workspaceDirectory)
	{
		if (string.IsNullOrWhiteSpace(workspaceDirectory))
		{
			throw new LlmException("worldbook_path", "世界书工作区路径不正确。");
		}
		string fullPath;
		try
		{
			fullPath = Path.GetFullPath(workspaceDirectory);
		}
		catch (Exception ex) when (((ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) ? 1 : 0) != 0)
		{
			throw new LlmException("worldbook_path", "世界书工作区路径不正确。");
		}
		path = Path.Combine(fullPath, "memory.json");
		legacyPath = Path.Combine(fullPath, "worldbook.json");
		gate = Gates.GetOrAdd(path, (string _) => new object());
	}

	public string EnsureFileForEditing()
	{
		lock (gate)
		{
			EnsureCreated();
			return path;
		}
	}

	public IReadOnlyList<WorldBookEntry> LoadOrCreate()
	{
		lock (gate)
		{
			EnsureCreated();
			return LoadCurrent().Entries;
		}
	}

	public IReadOnlyList<WorldBookMatch> Search(string query)
	{
		ArgumentNullException.ThrowIfNull(query, "query");
		lock (gate)
		{
			EnsureCreated();
			LoadedWorldBook loadedWorldBook = LoadCurrent();
			if (cachedRetriever == null || !string.Equals(cachedFingerprint, loadedWorldBook.Fingerprint, StringComparison.Ordinal))
			{
				cachedRetriever = new WorldBookRetriever(loadedWorldBook.Entries);
				cachedFingerprint = loadedWorldBook.Fingerprint;
			}
			return cachedRetriever.Search(query);
		}
	}

	private void EnsureCreated()
	{
		MigrateLegacyFile();
		if (File.Exists(path))
		{
			return;
		}
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path));
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException) ? 1 : 0) != 0)
		{
			throw new LlmException("worldbook_write", "创建世界书目录失败：" + ex.GetType().Name);
		}
		byte[] array;
		try
		{
			using Stream stream = typeof(WorldBookStore).Assembly.GetManifestResourceStream("Just_Lilith.DefaultWorldBook") ?? throw new InvalidOperationException("Default world-book resource is missing.");
			using MemoryStream memoryStream = new MemoryStream();
			stream.CopyTo(memoryStream);
			array = memoryStream.ToArray();
			Parse(array);
		}
		catch (LlmException)
		{
			throw;
		}
		catch (Exception ex3)
		{
			throw new LlmException("worldbook_resource", "内置世界书档案读取失败：" + ex3.GetType().Name);
		}
		string text = path + ".pending-" + Guid.NewGuid().ToString("N");
		try
		{
			using (FileStream fileStream = new FileStream(text, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
			{
				fileStream.Write(array, 0, array.Length);
				fileStream.Flush(flushToDisk: true);
			}
			try
			{
				File.Move(text, path);
			}
			catch (IOException) when (File.Exists(path))
			{
				File.Delete(text);
			}
		}
		catch (Exception ex5) when (((ex5 is IOException || ex5 is UnauthorizedAccessException || ex5 is ArgumentException || ex5 is NotSupportedException) ? 1 : 0) != 0)
		{
			TryDelete(text);
			throw new LlmException("worldbook_write", "创建世界书文件失败：" + ex5.GetType().Name);
		}
	}

	private void MigrateLegacyFile()
	{
		if (!File.Exists(legacyPath))
		{
			return;
		}
		try
		{
			if (!File.Exists(path))
			{
				File.Move(legacyPath, path);
				return;
			}
			if (!FilesAreIdentical(legacyPath, path))
			{
				throw new LlmException("worldbook_migration_conflict", "memory.json 与旧 worldbook.json 内容不同；两个文件均已保留，请手动合并后重试。");
			}
			File.Delete(legacyPath);
		}
		catch (LlmException)
		{
			throw;
		}
		catch (Exception ex2) when (((ex2 is IOException || ex2 is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			throw new LlmException("worldbook_write", "迁移世界书文件失败：" + ex2.GetType().Name);
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

	private LoadedWorldBook LoadCurrent()
	{
		byte[] array;
		try
		{
			using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			if (fileStream.Length > 16777216)
			{
				throw new LlmException("worldbook_size", $"世界书文件最多 {16777216} 字节。");
			}
			array = new byte[checked((int)fileStream.Length)];
			int num;
			for (int i = 0; i < array.Length; i += num)
			{
				num = fileStream.Read(array, i, array.Length - i);
				if (num == 0)
				{
					throw new EndOfStreamException();
				}
			}
		}
		catch (LlmException)
		{
			throw;
		}
		catch (Exception ex2) when (((ex2 is IOException || ex2 is UnauthorizedAccessException || ex2 is ArgumentException || ex2 is NotSupportedException || ex2 is OverflowException) ? 1 : 0) != 0)
		{
			throw new LlmException("worldbook_read", "读取世界书文件失败；请保存完成后重试。原因：" + ex2.GetType().Name);
		}
		string text = Convert.ToHexString(SHA256.HashData(array)).ToLowerInvariant();
		if (cachedEntries != null && string.Equals(cachedFingerprint, text, StringComparison.Ordinal))
		{
			return new LoadedWorldBook(cachedEntries, text);
		}
		IReadOnlyList<WorldBookEntry> entries = (cachedEntries = Parse(array));
		cachedFingerprint = text;
		cachedRetriever = null;
		return new LoadedWorldBook(entries, text);
	}

	private static IReadOnlyList<WorldBookEntry> Parse(byte[] source)
	{
		try
		{
			int num = ((source.Length >= 3 && source[0] == 239 && source[1] == 187 && source[2] == 191) ? 3 : 0);
			StrictUtf8.GetString(source, num, source.Length - num);
			using JsonDocument jsonDocument = JsonDocument.Parse(source.AsMemory(num), new JsonDocumentOptions
			{
				AllowTrailingCommas = false,
				CommentHandling = JsonCommentHandling.Disallow,
				MaxDepth = 8
			});
			if (jsonDocument.RootElement.ValueKind != JsonValueKind.Array)
			{
				throw FormatError("世界书顶层必须是档案数组。");
			}
			List<WorldBookEntry> list = new List<WorldBookEntry>();
			foreach (JsonElement item in jsonDocument.RootElement.EnumerateArray())
			{
				if (list.Count >= 10000)
				{
					throw FormatError($"世界书最多包含 {10000} 条档案。");
				}
				if (item.ValueKind != JsonValueKind.Object)
				{
					throw FormatError("每条世界书档案必须是 JSON 对象。");
				}
				string value = null;
				string value2 = null;
				string value3 = null;
				HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
				foreach (JsonProperty item2 in item.EnumerateObject())
				{
					if (!hashSet.Add(item2.Name))
					{
						throw FormatError("世界书档案包含重复字段：" + item2.Name);
					}
					if (item2.Value.ValueKind != JsonValueKind.String)
					{
						throw FormatError("世界书档案字段必须是字符串：" + item2.Name);
					}
					if (item2.NameEquals("category"))
					{
						value = item2.Value.GetString();
						continue;
					}
					if (item2.NameEquals("title"))
					{
						value2 = item2.Value.GetString();
						continue;
					}
					if (item2.NameEquals("content"))
					{
						value3 = item2.Value.GetString();
						continue;
					}
					throw FormatError("世界书档案包含未知字段：" + item2.Name);
				}
				value = ValidateField(value, "category", 64);
				value2 = ValidateField(value2, "title", 256);
				value3 = ValidateField(value3, "content", 4096);
				list.Add(new WorldBookEntry(value, value2, value3));
			}
			return Array.AsReadOnly(list.ToArray());
		}
		catch (LlmException)
		{
			throw;
		}
		catch (Exception ex2) when (((ex2 is JsonException || ex2 is DecoderFallbackException || ex2 is ArgumentException) ? 1 : 0) != 0)
		{
			throw new LlmException("worldbook_format", "世界书 JSON 格式不正确；原文件保持不变。原因：" + ex2.GetType().Name);
		}
	}

	private static string ValidateField(string? value, string name, int limit)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw FormatError("世界书档案缺少有效字段：" + name);
		}
		if (value.Length > limit)
		{
			throw FormatError($"世界书字段 {name} 最多 {limit} 个字符。");
		}
		if (value.Any(char.IsControl))
		{
			throw FormatError("世界书字段包含控制字符：" + name);
		}
		return value;
	}

	private static LlmException FormatError(string message)
	{
		return new LlmException("worldbook_format", message);
	}

	private static void TryDelete(string file)
	{
		try
		{
			if (File.Exists(file))
			{
				File.Delete(file);
			}
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}
}
