using System;
using System.IO;
using System.Linq;
using System.Text;
using Just_Lilith.Core.Llm;

namespace Just_Lilith.Core.Agent;

public sealed class AgentPersonaStore
{
	public const int ByteLimit = 65536;

	public const int CharacterLimit = 32768;

	private const string ResourceName = "Just_Lilith.DefaultAgentPersona";

	private static readonly UTF8Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

	private readonly bool _seedDefaultWhenMissing;

	public string FilePath { get; }

	public AgentPersonaStore(string filePath, bool seedDefaultWhenMissing = true)
	{
		FilePath = Path.GetFullPath(filePath);
		_seedDefaultWhenMissing = seedDefaultWhenMissing;
	}

	public void EnsureExists()
	{
		if (File.Exists(FilePath))
		{
			return;
		}
		if (!_seedDefaultWhenMissing)
		{
			throw new LlmException("agent_persona_missing", "指定的 AgentPersona.md 文件不存在：" + FilePath);
		}
		using Stream stream = typeof(AgentPersonaStore).Assembly.GetManifestResourceStream("Just_Lilith.DefaultAgentPersona") ?? throw new InvalidOperationException("AgentPersona.md 默认资源缺失，请重新构建插件。");
		Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
		string text = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			using (FileStream fileStream = new FileStream(text, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				stream.CopyTo(fileStream);
				fileStream.Flush(flushToDisk: true);
			}
			try
			{
				File.Move(text, FilePath, overwrite: false);
			}
			catch (IOException) when (File.Exists(FilePath))
			{
			}
		}
		finally
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
		}
	}

	public string Load()
	{
		try
		{
			EnsureExists();
			using FileStream fileStream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			if (fileStream.Length > 65536)
			{
				throw LimitError();
			}
			byte[] array = new byte[65537];
			int i;
			int num;
			for (i = 0; i < array.Length; i += num)
			{
				if ((num = fileStream.Read(array, i, array.Length - i)) == 0)
				{
					break;
				}
			}
			if (i > 65536)
			{
				throw LimitError();
			}
			int num2 = ((i >= 3 && array[0] == 239 && array[1] == 187 && array[2] == 191) ? 3 : 0);
			string text = Utf8.GetString(array, num2, i - num2);
			if (text.Length > 32768)
			{
				throw LimitError();
			}
			if (string.IsNullOrWhiteSpace(text))
			{
				throw new LlmException("agent_persona_empty", "AgentPersona.md 为空；请保存有效人格指令后重试。原文件已保留。");
			}
			if (text.Any(delegate(char c)
			{
				bool flag = char.IsControl(c);
				if (flag)
				{
					bool flag2;
					switch (c)
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
				throw new LlmException("agent_persona_text", "AgentPersona.md 含无效控制字符；原文件已保留。");
			}
			return text;
		}
		catch (DecoderFallbackException)
		{
			throw new LlmException("agent_persona_utf8", "AgentPersona.md 应保存为 UTF-8；原文件已保留。");
		}
		catch (Exception ex2) when (((ex2 is IOException || ex2 is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			throw new LlmException("agent_persona_io", "读取 AgentPersona.md 失败；请检查路径、权限或文件占用：" + FilePath);
		}
	}

	private static LlmException LimitError()
	{
		return new LlmException("agent_persona_limit", "AgentPersona.md 超过 64 KiB 或 32768 字符上限；原文件已保留。");
	}
}
