using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Just_Lilith.Core.Agent;

public sealed class AgentSettings
{
	public const string StateDisabled = "disabled";

	public const string StateIdle = "idle";

	public const string StateStarting = "starting";

	public const string StateWorking = "working";

	public const string StateCommand = "command";

	public const string StateEditing = "editing";

	public const string StateResponding = "responding";

	public const string StateCompleted = "completed";

	public const string StateCancelled = "cancelled";

	public const string StateFailed = "failed";

	private readonly object _sync = new object();

	private readonly string _defaultRoot;

	private readonly Func<string> _machineIdentity;

	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	private AgentOptions _options = new AgentOptions();

	private AgentThreadState _thread = new AgentThreadState();

	private AgentRuntimeStatus _status = new AgentRuntimeStatus("disabled");

	public string OptionsPath { get; }

	public string ThreadStatePath { get; }

	public string PersonaPath
	{
		get
		{
			string personaPath = Options.PersonaPath;
			if (!string.IsNullOrWhiteSpace(personaPath))
			{
				return Path.GetFullPath(Environment.ExpandEnvironmentVariables(personaPath), Path.GetDirectoryName(OptionsPath));
			}
			return Path.Combine(Path.GetDirectoryName(ThreadStatePath), "AgentPersona.md");
		}
	}

	public AgentOptions Options
	{
		get
		{
			lock (_sync)
			{
				return _options;
			}
		}
	}

	public bool Enabled => Options.Enabled;

	public string ProjectRoot => Options.ProjectRoot;

	public string Model => Options.Model;

	public string ReasoningEffort => Options.ReasoningEffort;

	public int TimeoutSeconds => Options.TimeoutSeconds;

	public string ConfiguredExecutable => Options.CodexExecutable;

	public string ConfiguredCodexHome => Options.CodexHome;

	public string CodexModelProvider => Options.CodexModelProvider;

	public string ApiKeyEnvironmentVariable => Options.ApiKeyEnvironmentVariable;

	public string ThreadId
	{
		get
		{
			lock (_sync)
			{
				return _thread.ThreadId;
			}
		}
	}

	public AgentRuntimeStatus RuntimeStatus => Volatile.Read(ref _status);

	public AgentPersonaStore CreatePersonaStore()
	{
		lock (_sync)
		{
			return new AgentPersonaStore(PersonaPath, string.IsNullOrWhiteSpace(_options.PersonaPath));
		}
	}

	public AgentSettings(string optionsPath, string threadStatePath, string projectRoot, Func<string>? machineIdentity = null)
	{
		OptionsPath = Path.GetFullPath(optionsPath);
		ThreadStatePath = Path.GetFullPath(threadStatePath);
		_defaultRoot = Path.GetFullPath(projectRoot);
		_machineIdentity = machineIdentity ?? ((Func<string>)(() => Environment.MachineName + "|" + Environment.UserDomainName + "|" + Environment.UserName));
		_options = new AgentOptions
		{
			ProjectRoot = _defaultRoot
		};
	}

	public void Reload()
	{
		lock (_sync)
		{
			AgentOptions value = (File.Exists(OptionsPath) ? Read<AgentOptions>(OptionsPath) : _options);
			value = Validate(value);
			string text = Owner(value);
			AgentThreadState agentThreadState = (File.Exists(ThreadStatePath) ? Read<AgentThreadState>(ThreadStatePath) : new AgentThreadState());
			if (agentThreadState.SchemaVersion != 1 || agentThreadState.Owner == null || agentThreadState.Transport == null || agentThreadState.ThreadId == null || agentThreadState.LegacyThreadId == null || (agentThreadState.ThreadId.Length != 0 && !IsThreadId(agentThreadState.ThreadId)))
			{
				throw new InvalidOperationException("Agent 会话文件格式无效；原文件已保留。");
			}
			if (agentThreadState.Owner != text || agentThreadState.Transport != "app-server-v3")
			{
				agentThreadState = new AgentThreadState
				{
					Owner = text,
					LegacyThreadId = ((agentThreadState.ThreadId.Length != 0) ? agentThreadState.ThreadId : agentThreadState.LegacyThreadId)
				};
			}
			_options = value;
			_thread = agentThreadState;
			if (!File.Exists(OptionsPath))
			{
				AtomicWrite(OptionsPath, value);
			}
			if (!File.Exists(ThreadStatePath) || Read<AgentThreadState>(ThreadStatePath) != agentThreadState)
			{
				AtomicWrite(ThreadStatePath, agentThreadState);
			}
		}
	}

	public void SetEnabled(bool enabled, bool reload = true)
	{
		lock (_sync)
		{
			if (reload)
			{
				Reload();
			}
			AgentOptions value = (File.Exists(OptionsPath) ? Read<AgentOptions>(OptionsPath) : _options);
			AgentOptions agentOptions = Validate(value)with
			{
				Enabled = enabled
			};
			AtomicWrite(OptionsPath, agentOptions);
			_options = (reload ? agentOptions : _options with
			{
				Enabled = enabled
			});
			SetRuntimeStatus(enabled ? "idle" : "disabled");
		}
	}

	public void SetModel(string model)
	{
		UpdateOptions((AgentOptions options) => options with
		{
			Model = (model ?? "")
		});
	}

	public void SetReasoningEffort(string reasoningEffort)
	{
		UpdateOptions((AgentOptions options) => options with
		{
			ReasoningEffort = (reasoningEffort ?? "")
		});
	}

	private void UpdateOptions(Func<AgentOptions, AgentOptions> update)
	{
		lock (_sync)
		{
			AgentOptions arg = Validate(File.Exists(OptionsPath) ? Read<AgentOptions>(OptionsPath) : _options);
			AgentOptions agentOptions = Validate(update(arg));
			AtomicWrite(OptionsPath, agentOptions);
			_options = agentOptions;
		}
	}

	public void SaveThreadId(string threadId)
	{
		if (!IsThreadId(threadId))
		{
			throw new InvalidOperationException("Agent 返回了无效会话标识。");
		}
		lock (_sync)
		{
			AgentThreadState agentThreadState = _thread with
			{
				ThreadId = threadId
			};
			AtomicWrite(ThreadStatePath, agentThreadState);
			_thread = agentThreadState;
		}
	}

	public void ClearThreadId()
	{
		lock (_sync)
		{
			AgentThreadState agentThreadState = _thread with
			{
				ThreadId = "",
				LegacyThreadId = _thread.ThreadId
			};
			AtomicWrite(ThreadStatePath, agentThreadState);
			_thread = agentThreadState;
		}
	}

	public void SetRuntimeStatus(string state, string? detail = null)
	{
		string text = (detail ?? "").Replace('\r', ' ').Replace('\n', ' ');
		Volatile.Write(ref _status, new AgentRuntimeStatus(state, (text.Length > 400) ? text.Substring(0, 400) : text));
	}

	public string GetThreadUri()
	{
		string threadId = ThreadId;
		if (!IsThreadId(threadId))
		{
			throw new InvalidOperationException("发送第一条 Agent 消息后才会建立专属对话。");
		}
		return "codex://threads/" + Uri.EscapeDataString(threadId);
	}

	public static bool IsThreadId(string? id)
	{
		if (!string.IsNullOrWhiteSpace(id) && id.Length <= 128)
		{
			return id.All(delegate(char c)
			{
				bool flag = AsciiLetter(c) || (c >= '0' && c <= '9');
				if (!flag)
				{
					bool flag2 = ((c == '-' || c == '_') ? true : false);
					flag = flag2;
				}
				return flag;
			});
		}
		return false;
	}

	private AgentOptions Validate(AgentOptions value)
	{
		if (value.SchemaVersion != 1)
		{
			throw new InvalidOperationException("Agent 配置版本不受支持；原文件已保留。");
		}
		string text = (string.IsNullOrWhiteSpace(value.ProjectRoot) ? _defaultRoot : Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.ProjectRoot), _defaultRoot));
		if (!Directory.Exists(text))
		{
			throw new DirectoryNotFoundException("Agent project_root 目录不存在。");
		}
		int timeoutSeconds = value.TimeoutSeconds;
		if ((timeoutSeconds < 30 || timeoutSeconds > 7200) ? true : false)
		{
			throw new InvalidOperationException("Agent timeout_seconds 范围为 30～7200。");
		}
		bool flag;
		switch (value.ReasoningEffort)
		{
		case "low":
		case "medium":
		case "high":
		case "xhigh":
		case "max":
		case "ultra":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			throw new InvalidOperationException("Agent reasoning_effort 无效。");
		}
		switch (value.ProviderMode)
		{
		case "auto":
		case "codex":
		case "saved_responses":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			throw new InvalidOperationException("Agent provider_mode 应为 auto、codex 或 saved_responses。");
		}
		string apiKeyEnvironmentVariable = value.ApiKeyEnvironmentVariable;
		if (string.IsNullOrEmpty(apiKeyEnvironmentVariable) || apiKeyEnvironmentVariable.Length > 128 || (!AsciiLetter(apiKeyEnvironmentVariable[0]) && apiKeyEnvironmentVariable[0] != '_') || apiKeyEnvironmentVariable.Any((char c) => !AsciiLetter(c) && (c < '0' || c > '9') && c != '_'))
		{
			throw new InvalidOperationException("Agent API Key 环境变量名无效。");
		}
		string[] array = new string[5] { value.Model, value.CodexExecutable, value.CodexHome, value.CodexModelProvider, value.PersonaPath };
		foreach (string text2 in array)
		{
			if (text2 == null || text2.Length > 2048 || text2.Any(char.IsControl))
			{
				throw new InvalidOperationException("Agent 配置字符串无效。");
			}
		}
		if (value.Model.Length > 256)
		{
			throw new InvalidOperationException("Agent model 长度超过 256。");
		}
		if (!string.IsNullOrWhiteSpace(value.PersonaPath))
		{
			Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.PersonaPath), Path.GetDirectoryName(OptionsPath));
		}
		return value with
		{
			ProjectRoot = text,
			Model = value.Model.Trim(),
			CodexExecutable = value.CodexExecutable.Trim(),
			CodexHome = value.CodexHome.Trim()
		};
	}

	private static bool AsciiLetter(char c)
	{
		switch (c)
		{
		case 'A':
		case 'B':
		case 'C':
		case 'D':
		case 'E':
		case 'F':
		case 'G':
		case 'H':
		case 'I':
		case 'J':
		case 'K':
		case 'L':
		case 'M':
		case 'N':
		case 'O':
		case 'P':
		case 'Q':
		case 'R':
		case 'S':
		case 'T':
		case 'U':
		case 'V':
		case 'W':
		case 'X':
		case 'Y':
		case 'Z':
		case 'a':
		case 'b':
		case 'c':
		case 'd':
		case 'e':
		case 'f':
		case 'g':
		case 'h':
		case 'i':
		case 'j':
		case 'k':
		case 'l':
		case 'm':
		case 'n':
		case 'o':
		case 'p':
		case 'q':
		case 'r':
		case 's':
		case 't':
		case 'u':
		case 'v':
		case 'w':
		case 'x':
		case 'y':
		case 'z':
			return true;
		default:
			return false;
		}
	}

	private string Owner(AgentOptions value)
	{
		string name = ((value.CodexHome.Length != 0) ? value.CodexHome : (Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex")));
		name = Path.GetFullPath(Environment.ExpandEnvironmentVariables(name), value.ProjectRoot);
		string s = _machineIdentity() + "|" + value.ProjectRoot.TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant() + "|" + name.TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
	}

	private static T Read<T>(string path)
	{
		if (new FileInfo(path).Length > 262144)
		{
			throw new InvalidOperationException("Agent 配置文件过大。");
		}
		T val = JsonSerializer.Deserialize<T>(File.ReadAllText(path));
		if (val == null)
		{
			throw new InvalidOperationException("Agent 配置为空。");
		}
		return val;
	}

	private static void AtomicWrite<T>(string path, T value)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		string text = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			File.WriteAllText(text, JsonSerializer.Serialize(value, Json), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			File.Move(text, path, overwrite: true);
		}
		finally
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
		}
	}
}
