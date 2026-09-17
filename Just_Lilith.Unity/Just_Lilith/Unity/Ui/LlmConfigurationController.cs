using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using Just_Lilith.Core.Agent;
using Just_Lilith.Core.Contracts;
using Just_Lilith.Core.Llm;
using Just_Lilith.Core.Speech;
using Just_Lilith.Core.Threading;
using Just_Lilith.Unity.Speech;

namespace Just_Lilith.Unity.Ui;

public sealed class LlmConfigurationController : IDisposable
{
	private static readonly string[] AgentModelChoices = new string[7] { "", "gpt-6-astra", "gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna", "gpt-5.5", "gpt-5.2" };

	private static readonly string[] AgentReasoningChoices = new string[6] { "low", "medium", "high", "xhigh", "max", "ultra" };

	private readonly AgentSettings _agentSettings;

	private readonly CodexAgentChannel _agent;

	private bool _agentReady;

	private bool _agentChat;

	private readonly IMainThreadDispatcher _mainThread;

	private readonly LlmConfigurationView _view;

	private readonly LlmSettingsStore _store;

	private readonly ConversationWorkspaceStore _conversation;

	private readonly ActiveConversationWorkspaceStore _activeConversation;

	private readonly WorldBookStore _worldBook;

	private readonly string _systemPromptPath;

	private readonly OpenAiCompatibleClient _client = new OpenAiCompatibleClient();

	private readonly DreamMemoryConsolidator _memory;

	private Task _memoryTask = Task.CompletedTask;

	private CancellationTokenSource? _memoryCancellation;

	private string? _memorySessionId;

	private readonly IOrdinaryChatOutputSink _output;

	private readonly IThinkingBubbleSink? _thinking;

	private readonly IPetStateSnapshotSource _petState;

	private readonly MainThreadSpeechController? _speech;

	private readonly ManualLogSource _log;

	private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

	private readonly Dictionary<string, IReadOnlyList<string>> _models = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

	private readonly Dictionary<string, LlmProfileDraft> _drafts = new Dictionary<string, LlmProfileDraft>(StringComparer.Ordinal);

	private readonly HashSet<string> _dirty = new HashSet<string>(StringComparer.Ordinal);

	private readonly Dictionary<string, string> _profileStatus = new Dictionary<string, string>(StringComparer.Ordinal);

	private CancellationTokenSource? _connectionCancellation;

	private CancellationTokenSource? _chatCancellation;

	private LlmSettings _settings = LlmSettings.CreateDefault();

	private bool _connectionBusy;

	private bool _chatBusy;

	private bool _chatCommitting;

	private bool _updating;

	private int _stopped;

	private long _chatGeneration;

	private string? _busyProfileId;

	private string _globalStatus = "";

	private string _reply = "";

	private AgentUiState ReadAgentUiState()
	{
		AgentRuntimeStatus runtimeStatus = _agentSettings.RuntimeStatus;
		return new AgentUiState(_agentSettings.Enabled, _agentChat, _agentReady, runtimeStatus.Label, runtimeStatus.Detail + "\n工作目录：" + _agentSettings.ProjectRoot + "\n提供方模式：" + _agentSettings.Options.ProviderMode + "\n配置：" + _agentSettings.OptionsPath + "\nAgent Persona：" + _agentSettings.PersonaPath, _agentSettings.ThreadId, _agentSettings.Model, _agentSettings.ReasoningEffort, GetAgentModelChoices(), AgentReasoningChoices);
	}

	private void ToggleAgent()
	{
		if (_connectionBusy || (_chatBusy && !_agentChat) || Volatile.Read(ref _stopped) != 0)
		{
			return;
		}
		try
		{
			if (!_agentSettings.Enabled && _agentChat)
			{
				return;
			}
			_agentSettings.SetEnabled(!_agentSettings.Enabled, !_agentChat);
			_agentReady = true;
			if (!_agentSettings.Enabled)
			{
				if (_agentChat)
				{
					CancelChat();
				}
				else
				{
					_agent.CancelActive();
				}
			}
			SetGlobalStatus(_agentSettings.Enabled ? "Agent 已开启；下一条消息进入独立 Codex 专属对话。" : "Agent 已关闭；后续消息恢复普通聊天。");
			_view.SetActiveSummary(DescribeActive());
		}
		catch (Exception error)
		{
			AgentError("切换 Agent", error);
		}
	}

	private IReadOnlyList<string> GetAgentModelChoices()
	{
		string model = _agentSettings.Model;
		if (string.IsNullOrWhiteSpace(model) || AgentModelChoices.Contains<string>(model, StringComparer.Ordinal))
		{
			return AgentModelChoices;
		}
		List<string> list = new List<string>();
		list.Add("");
		list.Add(model);
		list.AddRange(AgentModelChoices.Where((string text) => text.Length != 0));
		return list;
	}

	private void SelectAgentModel(string model)
	{
		if (_chatBusy || _connectionBusy || Volatile.Read(ref _stopped) != 0)
		{
			return;
		}
		try
		{
			_agentSettings.Reload();
			if (GetAgentModelChoices().Contains<string>(model, StringComparer.Ordinal))
			{
				_agentSettings.SetModel(model);
				SetGlobalStatus((model.Length == 0) ? "Agent 模型已设为 Codex 默认，下一轮生效。" : ("Agent 模型已设为 " + model + "，下一轮生效。"));
			}
		}
		catch (Exception error)
		{
			AgentError("选择 Agent 模型", error);
		}
	}

	private void SelectAgentReasoning(string reasoning)
	{
		if (_chatBusy || _connectionBusy || Volatile.Read(ref _stopped) != 0 || !AgentReasoningChoices.Contains<string>(reasoning, StringComparer.Ordinal))
		{
			return;
		}
		try
		{
			_agentSettings.Reload();
			_agentSettings.SetReasoningEffort(reasoning);
			SetGlobalStatus("Agent 推理强度已设为 " + reasoning + "，下一轮生效。");
		}
		catch (Exception error)
		{
			AgentError("选择 Agent 推理强度", error);
		}
	}

	private void OpenAgentSettings()
	{
		if (_chatBusy || _connectionBusy)
		{
			SetGlobalStatus("请等待当前请求结束后编辑 Agent 设置。");
			return;
		}
		try
		{
			if (!File.Exists(_agentSettings.OptionsPath))
			{
				_agentSettings.Reload();
			}
			Process.Start(new ProcessStartInfo("notepad.exe")
			{
				UseShellExecute = false,
				ArgumentList = { _agentSettings.OptionsPath }
			});
		}
		catch (Exception error)
		{
			AgentError("打开 Agent 设置", error);
		}
	}

	private void OpenAgentPersona()
	{
		if (_chatBusy || _connectionBusy || Volatile.Read(ref _stopped) != 0)
		{
			SetGlobalStatus("请等待当前请求结束后编辑 Agent Persona。");
			return;
		}
		try
		{
			_agentSettings.Reload();
			AgentPersonaStore agentPersonaStore = _agentSettings.CreatePersonaStore();
			agentPersonaStore.EnsureExists();
			if (Process.Start(new ProcessStartInfo("notepad.exe")
			{
				UseShellExecute = false,
				ArgumentList = { agentPersonaStore.FilePath }
			}) == null)
			{
				throw new InvalidOperationException("人格编辑器未启动。");
			}
			SetGlobalStatus("已打开 AgentPersona.md；保存后下一轮 Agent 对话生效，普通聊天 Persona 保持不变。");
		}
		catch (Exception error)
		{
			AgentError("打开 Agent Persona", error);
		}
	}

	private void AgentError(string action, Exception error)
	{
		_agentSettings.SetRuntimeStatus("failed", action + "失败：" + error.Message);
		SetGlobalStatus(_agentSettings.RuntimeStatus.Detail);
		_log.LogWarning("module=Agent; error=" + error.GetType().Name + "; text_logged=false");
	}

	private AgentProviderInput? ReadAgentProvider()
	{
		string model = _agentSettings.Model;
		if (string.IsNullOrWhiteSpace(model))
		{
			if (_agentSettings.Options.ProviderMode == "saved_responses")
			{
				throw new InvalidOperationException("请先在 Agent 模型菜单中选择模型，或使用 codex 提供方模式。");
			}
			return null;
		}
		LlmProfileSettings activeProfile = _store.Load().ActiveProfile;
		if (!activeProfile.IsConfigured || activeProfile.ApiFormat != LlmApiFormat.Responses)
		{
			return null;
		}
		return new AgentProviderInput(activeProfile.BaseUrl, new WindowsSecretProtector().Unprotect(activeProfile.ProtectedApiKey), model, _agentSettings.ReasoningEffort, activeProfile.ApiFormat);
	}

	private void BeginAgentSend(string input)
	{
		if (string.IsNullOrWhiteSpace(input))
		{
			SetGlobalStatus("请输入要发送的文字。");
			return;
		}
		_chatBusy = true;
		_agentChat = true;
		_chatCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
		long generation = Interlocked.Increment(ref _chatGeneration);
		SpeechLanguage language = _speech?.Settings.Language ?? SpeechLanguage.Chinese;
		bool voiceEnabled = _speech?.CanSpeak ?? false;
		long voiceGeneration = _speech?.Generation ?? 0;
		UpdateBusy();
		_thinking?.ShowThinking();
		SetGlobalStatus("正在请求莉莉丝 Agent；可在“莉莉丝”页暂停。");
		SendAgentAsync(input, generation, language, voiceEnabled, voiceGeneration, _chatCancellation);
	}

	private async Task SendAgentAsync(string input, long generation, SpeechLanguage language, bool voiceEnabled, long voiceGeneration, CancellationTokenSource operation)
	{
		try
		{
			ParsedChatReply parsed = AgentReply.Parse(await Task.Run(() => _agent.RequestAsync(input, language == SpeechLanguage.Japanese, operation.Token), operation.Token).ConfigureAwait(continueOnCapturedContext: false), language);
			await OnUi(delegate
			{
				if (!IsCurrentChat(generation, operation))
				{
					return;
				}
				OrdinaryChatOutput output = new OrdinaryChatOutput(Guid.NewGuid(), "agent", 0L, string.IsNullOrEmpty(_agentSettings.Model) ? "codex-default" : _agentSettings.Model, parsed.Text, DateTimeOffset.UtcNow, parsed.Speech, language, voiceEnabled && (_speech?.IsCurrentGeneration(voiceGeneration) ?? false), parsed.SpeechText, parsed.SpeechDiagnostic);
				_reply = parsed.Text;
				_view.SetReply(parsed.Text);
				_view.ClearChatInput();
				try
				{
					_output.Publish(output);
					SetGlobalStatus("Agent 已完成；回复已交给气泡/TTS，普通聊天记忆未变。" + ((parsed.SpeechDiagnostic == null) ? "" : (" " + parsed.SpeechDiagnostic)));
				}
				catch (Exception error2)
				{
					AgentError("Agent 回复已保存，但气泡交付", error2);
				}
			}).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (OperationCanceledException)
		{
			await OnUi(delegate
			{
				SetGlobalStatus("Agent 已取消；输入保留，已执行的工作可在专属对话查看。");
			}).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex2)
		{
			Exception error = ex2;
			await OnUi(delegate
			{
				AgentError("Agent 请求（输入保留，可打开专属对话检查）", error);
			}).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			await OnUi(delegate
			{
				_thinking?.EndThinking();
				if (_chatCancellation == operation)
				{
					_agentChat = false;
					_chatBusy = false;
					_chatCancellation = null;
					UpdateBusy();
				}
			}).ConfigureAwait(continueOnCapturedContext: false);
			operation.Dispose();
		}
	}

	public LlmConfigurationController(IMainThreadDispatcher mainThread, LlmConfigurationView view, string settingsPath, string workspacePath, ManualLogSource log, IOrdinaryChatOutputSink output, IPetStateSnapshotSource petState, MainThreadSpeechController? speech = null, string? agentProjectRoot = null)
	{
		_mainThread = mainThread;
		_view = view;
		_store = new LlmSettingsStore(settingsPath);
		_conversation = new ConversationWorkspaceStore(workspacePath);
		_activeConversation = new ActiveConversationWorkspaceStore(workspacePath);
		_worldBook = new WorldBookStore(workspacePath);
		_memory = new DreamMemoryConsolidator(_activeConversation, _client);
		_systemPromptPath = _conversation.SystemPromptPathOnDisk;
		_log = log;
		_output = output;
		_thinking = output as IThinkingBubbleSink;
		_petState = petState ?? throw new ArgumentNullException("petState");
		_speech = speech;
		_agentSettings = new AgentSettings(Path.Combine(Path.GetDirectoryName(settingsPath), "local.just_lilith.agent.json"), Path.Combine(workspacePath, "agent", "session.json"), agentProjectRoot ?? Environment.CurrentDirectory);
		_agent = new CodexAgentChannel(_agentSettings, ReadAgentProvider, delegate(string message)
		{
			_log.LogInfo("module=Agent; " + message);
		});
	}

	public void Initialize()
	{
		_view.Initialize(new LlmConfigurationCallbacks
		{
			SaveProfileRequested = SaveProfile,
			RefreshProfileRequested = RefreshProfile,
			CancelConnectionRequested = CancelConnection,
			MoveProfileRequested = MoveProfile,
			DraftChanged = DraftChanged,
			SendRequested = Send,
			ChatCancelRequested = CancelChat,
			OpenSystemPromptRequested = OpenSystemPrompt,
			OpenDreamMemoryRequested = OpenDreamMemory,
			OpenWorldBookRequested = OpenWorldBook,
			ListConversationsRequested = ListConversations,
			StartConversationRequested = StartNewConversation,
			SwitchConversationRequested = SwitchConversation,
			RenameConversationRequested = RenameConversation,
			DeleteConversationRequested = DeleteConversation,
			AgentToggleRequested = ToggleAgent,
			AgentSettingsRequested = OpenAgentSettings,
			AgentPersonaRequested = OpenAgentPersona,
			AgentModelSelectedRequested = SelectAgentModel,
			AgentReasoningSelectedRequested = SelectAgentReasoning,
			AgentStatusRequested = ReadAgentUiState,
			Stopped = Dispose
		});
		_connectionBusy = true;
		UpdateBusy();
		LoadAsync();
	}

	private async Task LoadAsync()
	{
		try
		{
			LlmSettings loaded = await Task.Run((Func<LlmSettings>)_store.Load).ConfigureAwait(continueOnCapturedContext: false);
			try
			{
				await Task.Run((Action)_agentSettings.Reload).ConfigureAwait(continueOnCapturedContext: false);
				_agentSettings.SetRuntimeStatus(_agentSettings.Enabled ? "idle" : "disabled");
				_agentReady = true;
			}
			catch (Exception ex)
			{
				_agentSettings.SetRuntimeStatus("failed", "Agent 配置读取失败：" + ex.Message);
			}
			LlmException promptError = null;
			LlmException worldBookError = null;
			try
			{
				await Task.Run((Func<string>)_conversation.LoadSystemPrompt).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (LlmException ex2)
			{
				promptError = ex2;
			}
			try
			{
				await Task.Run((Func<IReadOnlyList<WorldBookEntry>>)_worldBook.LoadOrCreate).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (LlmException ex3)
			{
				worldBookError = ex3;
			}
			await OnUi(delegate
			{
				_settings = loaded;
				_globalStatus = (loaded.Profiles.Any((LlmProfileSettings p) => p.HasApiKey) ? "已载入三套配置；模型列表尚未刷新。启动时未联网。" : "请在三套配置中填写 URL 与 API Key，再逐行保存并检测模型。");
				if (promptError != null)
				{
					_globalStatus = "系统提示词文件需要检查：" + promptError.Message;
				}
				if (worldBookError != null)
				{
					_globalStatus = "世界书文件需要检查：" + worldBookError.Message;
				}
				PresentAll(clearAllKeys: true);
				_log.LogInfo("module=UI; state=configured; panel=llm_configuration; entry=awaiting_other_tab_mount; hotkey=F7");
				_log.LogInfo("module=LLM; state=configurable; profiles=3; startup_network=false");
				ManualLogSource log = _log;
				BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(58, 1, out var isEnabled);
				if (isEnabled)
				{
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("module=LLM; event=system_prompt_workspace_checked; status=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted((promptError == null) ? "ready" : promptError.Code);
				}
				log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
				ManualLogSource log2 = _log;
				bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(54, 1, out isEnabled);
				if (isEnabled)
				{
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("module=LLM; event=worldbook_workspace_checked; status=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted((worldBookError == null) ? "ready" : worldBookError.Code);
				}
				log2.LogInfo(bepInExInfoLogInterpolatedStringHandler);
			}).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception error)
		{
			await Report(error, "读取配置", null).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			await OnUi(delegate
			{
				_connectionBusy = false;
				_busyProfileId = null;
				UpdateBusy();
			}).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	private void SaveProfile(LlmProfileDraft rawDraft)
	{
		if (!_connectionBusy && !_chatBusy && Volatile.Read(ref _stopped) == 0 && TryFindProfile(rawDraft.ProfileId, out LlmProfileSettings profile))
		{
			LlmProfileDraft llmProfileDraft = NormalizeReasoningDraft(rawDraft, profile);
			_connectionBusy = true;
			_busyProfileId = llmProfileDraft.ProfileId;
			_connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
			UpdateBusy();
			SetProfileStatus(llmProfileDraft.ProfileId, "正在原子保存此配置…");
			LlmProfileSettings replacement = profile with
			{
				BaseUrl = llmProfileDraft.BaseUrl,
				ModelId = llmProfileDraft.Model,
				ApiFormat = ParseProtocol(llmProfileDraft.Protocol),
				ReasoningMode = ParseMode(llmProfileDraft.ReasoningMode),
				CustomReasoningEffort = llmProfileDraft.ReasoningEffort
			};
			LlmSettings proposed = ReplaceProfile(_settings, replacement);
			ApiKeyUpdate apiKeyUpdate = (string.IsNullOrWhiteSpace(llmProfileDraft.ApiKey) ? ApiKeyUpdate.Keep : ApiKeyUpdate.ReplaceWith(llmProfileDraft.ApiKey));
			bool connectionIdentityChanged = apiKeyUpdate.Mode == ApiKeyUpdateMode.Replace || !EquivalentUrl(llmProfileDraft.BaseUrl, profile.BaseUrl);
			SaveAndDiscoverAsync(proposed, llmProfileDraft.ProfileId, apiKeyUpdate, connectionIdentityChanged, _connectionCancellation);
		}
	}

	private async Task SaveAndDiscoverAsync(LlmSettings proposed, string profileId, ApiKeyUpdate keyUpdate, bool connectionIdentityChanged, CancellationTokenSource operation)
	{
		bool savedToDisk = false;
		try
		{
			Dictionary<string, ApiKeyUpdate> updates = new Dictionary<string, ApiKeyUpdate>(StringComparer.Ordinal) { [profileId] = keyUpdate };
			LlmSettings saved = await Task.Run(delegate
			{
				operation.Token.ThrowIfCancellationRequested();
				return _store.Save(proposed, updates);
			}).ConfigureAwait(continueOnCapturedContext: false);
			savedToDisk = true;
			await OnUi(delegate
			{
				LlmProfileSettings llmProfileSettings = FindProfile(_settings, profileId);
				if (!string.Equals(b: FindProfile(saved, profileId).BaseUrl, a: llmProfileSettings.BaseUrl, comparisonType: StringComparison.Ordinal) || keyUpdate.Mode == ApiKeyUpdateMode.Replace)
				{
					_models.Remove(profileId);
				}
				_settings = saved;
				_dirty.Remove(profileId);
				_drafts.Remove(profileId);
				_profileStatus[profileId] = "配置已保存；正在获取模型列表…";
				_view.ApplyProfileState(CreateProfileState(profileId), clearKey: true);
				_view.SetActiveSummary(DescribeActive());
				ManualLogSource log = _log;
				BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(58, 2, out var isEnabled);
				if (isEnabled)
				{
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("module=LLM; event=configuration_saved; profile=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(SafeProfile(profileId));
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; revision=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(saved.Revision);
				}
				log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
			}).ConfigureAwait(continueOnCapturedContext: false);
			await DiscoverAndSelectAsync(saved, profileId, operation.Token, connectionIdentityChanged).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (OperationCanceledException)
		{
			await OnUi(delegate
			{
				SetProfileStatus(profileId, savedToDisk ? "配置已保存；模型列表获取已取消。" : "保存操作已取消。");
			}).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception error)
		{
			await Report(error, savedToDisk ? "配置已保存；获取模型列表" : "保存配置", profileId).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			await OnUi(delegate
			{
				_connectionBusy = false;
				_busyProfileId = null;
				_connectionCancellation = null;
				UpdateBusy();
			}).ConfigureAwait(continueOnCapturedContext: false);
			operation.Dispose();
		}
	}

	private void RefreshProfile(string profileId)
	{
		if (!_connectionBusy && !_chatBusy && Volatile.Read(ref _stopped) == 0 && TryFindProfile(profileId, out LlmProfileSettings profile))
		{
			if (_dirty.Contains(profileId))
			{
				SetProfileStatus(profileId, "此行有未保存修改；请先保存。刷新只使用已保存连接。");
				return;
			}
			if (!profile.HasApiKey || string.IsNullOrWhiteSpace(profile.BaseUrl))
			{
				SetProfileStatus(profileId, "请先保存此行的 URL 与 API Key。");
				return;
			}
			_connectionBusy = true;
			_busyProfileId = profileId;
			_connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
			UpdateBusy();
			SetProfileStatus(profileId, "正在获取此行已保存连接的模型列表…");
			DiscoverAsync(_settings, profileId, _connectionCancellation);
		}
	}

	private async Task DiscoverAsync(LlmSettings snapshot, string profileId, CancellationTokenSource operation)
	{
		try
		{
			await DiscoverAndSelectAsync(snapshot, profileId, operation.Token).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (OperationCanceledException)
		{
			await OnUi(delegate
			{
				SetProfileStatus(profileId, "模型列表获取已取消；已保存配置未变。");
			}).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception error)
		{
			await Report(error, "获取模型列表", profileId).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			await OnUi(delegate
			{
				_connectionBusy = false;
				_busyProfileId = null;
				_connectionCancellation = null;
				UpdateBusy();
			}).ConfigureAwait(continueOnCapturedContext: false);
			operation.Dispose();
		}
	}

	private async Task DiscoverAndSelectAsync(LlmSettings snapshot, string profileId, CancellationToken token, bool connectionIdentityChanged = false)
	{
		LlmProfileSettings profile = FindProfile(snapshot, profileId);
		IReadOnlyList<string> models = await _client.ListModelsAsync(profile, token).ConfigureAwait(continueOnCapturedContext: false);
		token.ThrowIfCancellationRequested();
		bool hadSelection = !string.IsNullOrWhiteSpace(profile.ModelId);
		bool selectionListed = hadSelection && models.Contains<string>(profile.ModelId, StringComparer.Ordinal);
		bool reselectForNewConnection = (connectionIdentityChanged & hadSelection) && !selectionListed;
		string selected = ((!hadSelection | reselectForNewConnection) ? models[0] : profile.ModelId);
		IReadOnlyList<string> allowedEfforts = ReasoningPolicy.Evaluate(selected, ReasoningMode.Omit).AllowedEfforts;
		string customReasoningEffort = ((profile.ReasoningMode == ReasoningMode.Custom && string.Equals(selected, profile.ModelId, StringComparison.Ordinal) && allowedEfforts.Contains<string>(profile.CustomReasoningEffort, StringComparer.Ordinal)) ? profile.CustomReasoningEffort : allowedEfforts[0]);
		LlmProfileSettings llmProfileSettings = profile with
		{
			ModelId = selected,
			CustomReasoningEffort = customReasoningEffort
		};
		LlmSettings committed = snapshot;
		if (llmProfileSettings != profile)
		{
			LlmSettings proposed = ReplaceProfile(snapshot, llmProfileSettings);
			LlmSettings llmSettings = await Task.Run(delegate
			{
				token.ThrowIfCancellationRequested();
				return _store.Save(proposed);
			}).ConfigureAwait(continueOnCapturedContext: false);
			committed = llmSettings;
		}
		await OnUi(delegate
		{
			_settings = committed;
			_models[profileId] = models;
			_dirty.Remove(profileId);
			_drafts.Remove(profileId);
			_profileStatus[profileId] = (reselectForNewConnection ? $"检测到 {models.Count} 个模型；原连接模型不在新连接列表中，已明确改用首个模型 {selected}。" : ((hadSelection && !selectionListed) ? $"检测到 {models.Count} 个模型；已保存模型 {selected} 不在本次列表中，已保留且未静默替换。请用箭头明确选择后保存，或按服务商说明继续使用。" : $"检测到 {models.Count} 个模型；已选 {selected}。列表可见不代表聊天调用已验证。"));
			_view.ApplyProfileState(CreateProfileState(profileId), clearKey: true);
			_view.SetActiveSummary(DescribeActive());
			ManualLogSource log = _log;
			BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(127, 4, out var isEnabled);
			if (isEnabled)
			{
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("module=LLM; event=models_listed; profile=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(SafeProfile(profileId));
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; count=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(models.Count);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; selected_persisted=true; selected_listed=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(selectionListed || !hadSelection);
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; reselected_for_connection_change=");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted(reselectForNewConnection);
			}
			log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
		}).ConfigureAwait(continueOnCapturedContext: false);
	}

	private void MoveProfile(string profileId, int delta)
	{
		bool flag = _connectionBusy || _chatBusy || Volatile.Read(ref _stopped) != 0;
		if (!flag)
		{
			bool flag2 = ((delta == -1 || delta == 1) ? true : false);
			flag = !flag2;
		}
		if (flag)
		{
			return;
		}
		if (_dirty.Count != 0)
		{
			SetGlobalStatus("调整优先级前，请先保存所有已修改的配置行。");
			return;
		}
		int num = Array.FindIndex(_settings.Profiles, (LlmProfileSettings p) => p.ProfileId == profileId);
		int num2 = num + delta;
		if (num >= 0 && num2 >= 0 && num2 < _settings.Profiles.Length)
		{
			LlmProfileSettings[] array = _settings.Profiles.Select((LlmProfileSettings p) => p with { }).ToArray();
			ref LlmProfileSettings reference = ref array[num];
			ref LlmProfileSettings reference2 = ref array[num2];
			LlmProfileSettings llmProfileSettings = array[num2];
			LlmProfileSettings llmProfileSettings2 = array[num];
			reference = llmProfileSettings;
			reference2 = llmProfileSettings2;
			LlmSettings proposed = _settings with
			{
				Profiles = array
			};
			_connectionBusy = true;
			_busyProfileId = profileId;
			_connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
			UpdateBusy();
			SetGlobalStatus("正在保存配置优先级…");
			MoveAsync(proposed, _connectionCancellation);
		}
	}

	private async Task MoveAsync(LlmSettings proposed, CancellationTokenSource operation)
	{
		try
		{
			LlmSettings saved = await Task.Run(delegate
			{
				operation.Token.ThrowIfCancellationRequested();
				return _store.Save(proposed);
			}).ConfigureAwait(continueOnCapturedContext: false);
			await OnUi(delegate
			{
				_settings = saved;
				_globalStatus = "优先级已保存；普通聊天严格使用最上方配置，不自动改用其他地址。";
				PresentAll(clearAllKeys: false);
				ManualLogSource log = _log;
				BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(48, 1, out var isEnabled);
				if (isEnabled)
				{
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("module=LLM; event=profile_order_saved; revision=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(saved.Revision);
				}
				log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
			}).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (OperationCanceledException)
		{
			await OnUi(delegate
			{
				SetGlobalStatus("优先级调整已取消。");
			}).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception error)
		{
			await Report(error, "保存优先级", null).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			await OnUi(delegate
			{
				_connectionBusy = false;
				_busyProfileId = null;
				_connectionCancellation = null;
				UpdateBusy();
			}).ConfigureAwait(continueOnCapturedContext: false);
			operation.Dispose();
		}
	}

	private void DraftChanged(LlmProfileDraft rawDraft)
	{
		if (_updating || Volatile.Read(ref _stopped) != 0 || !TryFindProfile(rawDraft.ProfileId, out LlmProfileSettings profile))
		{
			return;
		}
		LlmProfileDraft llmProfileDraft = NormalizeReasoningDraft(rawDraft, profile);
		_drafts[llmProfileDraft.ProfileId] = llmProfileDraft;
		bool flag = !string.IsNullOrWhiteSpace(llmProfileDraft.ApiKey) || !EquivalentUrl(llmProfileDraft.BaseUrl, profile.BaseUrl);
		if (flag)
		{
			_models.Remove(llmProfileDraft.ProfileId);
		}
		bool flag2 = flag || llmProfileDraft.Model != profile.ModelId || ParseProtocol(llmProfileDraft.Protocol) != profile.ApiFormat || ParseMode(llmProfileDraft.ReasoningMode) != profile.ReasoningMode || llmProfileDraft.ReasoningEffort != profile.CustomReasoningEffort;
		if (flag2)
		{
			_dirty.Add(llmProfileDraft.ProfileId);
		}
		else
		{
			_dirty.Remove(llmProfileDraft.ProfileId);
			_drafts.Remove(llmProfileDraft.ProfileId);
		}
		_profileStatus[llmProfileDraft.ProfileId] = ((!flag2) ? "此行与已保存值一致；模型列表需要时请显式刷新。" : (flag ? "连接信息已修改；旧模型列表已清除。保存并重新检测后才会用于请求。" : "此行已修改；保存后才会用于请求。"));
		_updating = true;
		try
		{
			_view.ApplyProfileState(CreateProfileState(llmProfileDraft.ProfileId), clearKey: false);
		}
		finally
		{
			_updating = false;
		}
	}

	private void Send(string input)
	{
		if (_connectionBusy || _chatBusy || Volatile.Read(ref _stopped) != 0)
		{
			return;
		}
		try
		{
			_agentSettings.Reload();
			_agentReady = true;
		}
		catch (Exception ex)
		{
			_agentSettings.SetRuntimeStatus("failed", "Agent 配置读取失败：" + ex.Message);
			if (_agentSettings.Enabled)
			{
				SetGlobalStatus(_agentSettings.RuntimeStatus.Detail);
				return;
			}
		}
		if (_agentReady && _agentSettings.Enabled)
		{
			BeginAgentSend(input);
			return;
		}
		LlmProfileSettings activeProfile = _settings.ActiveProfile;
		if (_dirty.Contains(activeProfile.ProfileId))
		{
			SetGlobalStatus("最上方配置有未保存修改；请先保存后再发送。");
			return;
		}
		if (!activeProfile.IsConfigured)
		{
			SetGlobalStatus("请先完成并保存最上方配置、API Key 与模型。");
			return;
		}
		if (string.IsNullOrWhiteSpace(input))
		{
			SetGlobalStatus("请输入要发送的文字。");
			return;
		}
		_chatBusy = true;
		_chatCommitting = false;
		_chatCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
		long generation = Interlocked.Increment(ref _chatGeneration);
		DateTimeOffset utcNow = DateTimeOffset.UtcNow;
		PetStateSnapshot petState;
		try
		{
			petState = _petState.CapturePetState();
		}
		catch (Exception ex2)
		{
			ManualLogSource log = _log;
			BepInExWarningLogInterpolatedStringHandler bepInExWarningLogInterpolatedStringHandler = new BepInExWarningLogInterpolatedStringHandler(81, 1, out var isEnabled);
			if (isEnabled)
			{
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral("module=PetState; state=capture_failed; code=");
				bepInExWarningLogInterpolatedStringHandler.AppendFormatted(ex2.GetType().Name);
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral("; fallback=unknown; text_logged=false");
			}
			log.LogWarning(bepInExWarningLogInterpolatedStringHandler);
			petState = PetStateContextBuilder.CreateSnapshot(new PetStateObservation(utcNow));
		}
		UpdateBusy();
		_thinking?.ShowThinking();
		SetGlobalStatus("正在请求最上方的已保存配置；系统提示词和近期会话由后台载入。");
		SpeechLanguage speechLanguage = _speech?.Settings.Language ?? SpeechLanguage.Chinese;
		bool speechEnabled = _speech?.CanSpeak ?? false;
		long speechGeneration = _speech?.Generation ?? 0;
		SendAsync(activeProfile, _settings.Revision, input, utcNow, petState, generation, speechLanguage, speechEnabled, speechGeneration, _chatCancellation);
	}

	public string StartNewConversation()
	{
		if (Volatile.Read(ref _stopped) != 0 || _chatBusy || _chatCommitting)
		{
			throw new LlmException("conversation_busy", "当前对话尚未结束，请稍后新建会话。");
		}
		LlmProfileSettings activeProfile = _settings.ActiveProfile;
		if (!activeProfile.IsConfigured)
		{
			throw new LlmException("conversation_unconfigured", "请先载入已保存的对话配置，再新建会话。");
		}
		if (_activeConversation.ListSessions().Count == 0)
		{
			_activeConversation.LoadOrCreate(activeProfile.ProfileId, activeProfile.ModelId, activeProfile.BaseUrl, activeProfile.ProtectedApiKey);
		}
		ActiveConversationSnapshot activeConversationSnapshot = _activeConversation.StartNewSession();
		Cancel(_memoryCancellation);
		_reply = "";
		_view.SetReply("");
		return activeConversationSnapshot.SessionId;
	}

	public IReadOnlyList<ActiveConversationInfo> ListConversations()
	{
		if (Volatile.Read(ref _stopped) != 0)
		{
			throw new LlmException("conversation_busy", "插件已停止。");
		}
		IReadOnlyList<ActiveConversationInfo> readOnlyList = _activeConversation.ListSessions();
		if (readOnlyList.Count != 0)
		{
			return readOnlyList;
		}
		LlmProfileSettings activeProfile = _settings.ActiveProfile;
		if (activeProfile.IsConfigured)
		{
			_activeConversation.LoadOrCreate(activeProfile.ProfileId, activeProfile.ModelId, activeProfile.BaseUrl, activeProfile.ProtectedApiKey);
		}
		return _activeConversation.ListSessions();
	}

	public string SwitchConversation(string sessionId, string? expectedActiveSessionId = null)
	{
		if (Volatile.Read(ref _stopped) != 0 || _chatBusy || _chatCommitting)
		{
			throw new LlmException("conversation_busy", "当前对话尚未结束，请稍后切换会话。");
		}
		ActiveConversationSnapshot activeConversationSnapshot = _activeConversation.SwitchSession(sessionId, expectedActiveSessionId);
		if (!string.Equals(_memorySessionId, activeConversationSnapshot.SessionId, StringComparison.Ordinal))
		{
			Cancel(_memoryCancellation);
		}
		_reply = activeConversationSnapshot.Conversation.Turns.LastOrDefault()?.Assistant ?? "";
		_view.SetReply(_reply);
		return activeConversationSnapshot.SessionId;
	}

	public string RenameConversation(string sessionId, string displayName)
	{
		if (Volatile.Read(ref _stopped) != 0)
		{
			throw new LlmException("conversation_busy", "插件已停止。");
		}
		return _activeConversation.RenameSession(sessionId, displayName).DisplayName;
	}

	public string DeleteConversation(string sessionId, string? expectedActiveSessionId = null)
	{
		if (Volatile.Read(ref _stopped) != 0 || _chatBusy || _chatCommitting || !_memoryTask.IsCompleted)
		{
			throw new LlmException("conversation_busy", "当前对话或幻境记忆整理尚未结束，请稍后删除幻境。");
		}
		ActiveConversationSnapshot activeConversationSnapshot = _activeConversation.DeleteSession(sessionId, expectedActiveSessionId);
		Cancel(_memoryCancellation);
		_memorySessionId = null;
		_reply = activeConversationSnapshot.Conversation.Turns.LastOrDefault()?.Assistant ?? "";
		_view.SetReply(_reply);
		return activeConversationSnapshot.SessionId;
	}

	private void OpenSystemPrompt()
	{
		if (Volatile.Read(ref _stopped) == 0)
		{
			_conversation.LoadSystemPrompt();
			if (Process.Start(new ProcessStartInfo("notepad.exe")
			{
				UseShellExecute = false,
				ArgumentList = { _systemPromptPath }
			}) == null)
			{
				throw new LlmException("system_prompt_open", "打开系统提示词文件失败。");
			}
			SetGlobalStatus("已打开系统提示词；保存文件后下一轮对话生效。");
		}
	}

	private void OpenDreamMemory()
	{
		if (Volatile.Read(ref _stopped) != 0)
		{
			return;
		}
		try
		{
			if (_chatBusy || _chatCommitting || !_memoryTask.IsCompleted)
			{
				throw new LlmException("conversation_busy", "当前对话或幻境记忆整理尚未结束，请完成后再打开编辑。");
			}
			if (_activeConversation.ListSessions().Count == 0)
			{
				LlmProfileSettings activeProfile = _settings.ActiveProfile;
				if (!activeProfile.IsConfigured)
				{
					throw new LlmException("conversation_unconfigured", "请先载入已保存的对话配置，再打开幻境记忆。");
				}
				_activeConversation.LoadOrCreate(activeProfile.ProfileId, activeProfile.ModelId, activeProfile.BaseUrl, activeProfile.ProtectedApiKey);
			}
			string activeSessionFilePath = _activeConversation.GetActiveSessionFilePath();
			ProcessStartInfo processStartInfo = new ProcessStartInfo("notepad.exe")
			{
				UseShellExecute = false
			};
			processStartInfo.ArgumentList.Add(activeSessionFilePath);
			try
			{
				if (Process.Start(processStartInfo) == null)
				{
					throw new InvalidOperationException("Notepad did not start.");
				}
			}
			catch (Exception ex) when (((ex is Win32Exception || ex is InvalidOperationException || ex is IOException) ? 1 : 0) != 0)
			{
				throw new LlmException("dream_memory_open", "打开当前幻境记忆文件失败。");
			}
			SetGlobalStatus("已打开当前幻境记忆；保存后下一次发送或整理会热读取。编辑期间请勿发送消息。");
		}
		catch (LlmException ex2)
		{
			SetGlobalStatus("打开幻境记忆失败：" + ex2.Message);
			throw;
		}
	}

	private void OpenWorldBook()
	{
		if (Volatile.Read(ref _stopped) != 0)
		{
			return;
		}
		try
		{
			string item = _worldBook.EnsureFileForEditing();
			ProcessStartInfo processStartInfo = new ProcessStartInfo("notepad.exe")
			{
				UseShellExecute = false
			};
			processStartInfo.ArgumentList.Add(item);
			try
			{
				if (Process.Start(processStartInfo) == null)
				{
					throw new InvalidOperationException("Notepad did not start.");
				}
			}
			catch (Exception ex) when (((ex is Win32Exception || ex is InvalidOperationException || ex is IOException) ? 1 : 0) != 0)
			{
				throw new LlmException("worldbook_open", "打开世界书文件失败。");
			}
			SetGlobalStatus("已打开回忆；保存后下一轮对话会重新校验并热读取世界书。");
		}
		catch (LlmException ex2)
		{
			SetGlobalStatus("打开回忆失败：" + ex2.Message);
			throw;
		}
	}

	private async Task SendAsync(LlmProfileSettings profile, long revision, string input, DateTimeOffset sentAtUtc, PetStateSnapshot petState, long generation, SpeechLanguage speechLanguage, bool speechEnabled, long speechGeneration, CancellationTokenSource operation)
	{
		try
		{
			try
			{
				await _memoryTask.WaitAsync(operation.Token).ConfigureAwait(continueOnCapturedContext: false);
				(ActiveConversationSnapshot Session, IReadOnlyList<WorldBookMatch> WorldBookMatches) prepared = await Task.Run(delegate
				{
					ActiveConversationSnapshot activeConversationSnapshot = _activeConversation.LoadOrCreate(profile.ProfileId, profile.ModelId, profile.BaseUrl, profile.ProtectedApiKey);
					ActiveConversationSnapshot item;
					try
					{
						item = _activeConversation.EnsureCanAppend(activeConversationSnapshot.SessionId, activeConversationSnapshot.Conversation.Revision, input);
					}
					catch (LlmException ex2) when (ex2.Code == "memory_pending")
					{
						item = activeConversationSnapshot;
					}
					IReadOnlyList<WorldBookMatch> item2 = _worldBook.Search(input);
					return (Session: item, WorldBookMatches: item2);
				}, operation.Token).ConfigureAwait(continueOnCapturedContext: false);
				ActiveConversationSnapshot loadedSession = await Task.Run(() => _memory.ConsolidateAsync(prepared.Session, profile, operation.Token), operation.Token).ConfigureAwait(continueOnCapturedContext: false);
				(string SystemPrompt, ActiveConversationSnapshot Session, IReadOnlyList<ChatMessage> Messages, string MemoryReference, string CurrentReference, int WorldBookMatchCount) context = await Task.Run(delegate
				{
					operation.Token.ThrowIfCancellationRequested();
					string item = _conversation.LoadSystemPrompt();
					ActiveConversationSnapshot activeConversationSnapshot = _activeConversation.EnsureCanAppend(loadedSession.SessionId, loadedSession.Conversation.Revision, input);
					IReadOnlyList<ChatMessage> item2 = ConversationContextBuilder.BuildRecent(activeConversationSnapshot, input);
					string item3 = ConversationContextBuilder.BuildMemoryReference(activeConversationSnapshot);
					string item4 = PetStateContextBuilder.CombineWithWorldBook(WorldBookContextBuilder.Build(prepared.WorldBookMatches), petState);
					operation.Token.ThrowIfCancellationRequested();
					return (SystemPrompt: item, Session: activeConversationSnapshot, Messages: item2, MemoryReference: item3, CurrentReference: item4, WorldBookMatchCount: prepared.WorldBookMatches.Count);
				}, operation.Token).ConfigureAwait(continueOnCapturedContext: false);
				ParsedChatReply parsedReply = StructuredChatReply.Parse(await _client.CompleteAsync(profile, context.Messages, StructuredChatReply.BuildSystemPrompt(context.SystemPrompt, speechLanguage), operation.Token, speechLanguage == SpeechLanguage.Japanese, context.MemoryReference, context.CurrentReference).ConfigureAwait(continueOnCapturedContext: false), speechLanguage);
				string reply = parsedReply.Text;
				DateTimeOffset receivedAtUtc = DateTimeOffset.UtcNow;
				bool commitAccepted = false;
				await OnUi(delegate
				{
					if (IsCurrentChat(generation, operation))
					{
						_chatCommitting = true;
						commitAccepted = true;
						SetGlobalStatus("已收到回复；正在后台保存完整会话回合…");
					}
				}).ConfigureAwait(continueOnCapturedContext: false);
				if (!commitAccepted)
				{
					goto end_IL_014f;
				}
				ActiveConversationSnapshot saved = await Task.Run(() => _activeConversation.AppendCompletedTurn(context.Session.SessionId, context.Session.Conversation.Revision, input, sentAtUtc, reply, receivedAtUtc, context.Session.EventsFingerprint, context.Session.ActivationToken)).ConfigureAwait(continueOnCapturedContext: false);
				await OnUi(delegate
				{
					if (IsCurrentChat(generation, operation))
					{
						OrdinaryChatOutput output = new OrdinaryChatOutput(Guid.NewGuid(), profile.ProfileId, revision, profile.ModelId, reply, receivedAtUtc, parsedReply.Speech, speechLanguage, speechEnabled && (_speech?.IsCurrentGeneration(speechGeneration) ?? false), parsedReply.SpeechText, parsedReply.SpeechDiagnostic);
						_reply = reply;
						_view.SetReply(reply);
						bool isEnabled;
						try
						{
							_output.Publish(output);
							_globalStatus = "已保存本轮对话；同一回复已交给气泡和 TTS 文本协调桥。";
							if (parsedReply.SpeechDiagnostic != null)
							{
								_globalStatus = _globalStatus + " 本轮语音未生成：" + parsedReply.SpeechDiagnostic;
								ManualLogSource log = _log;
								string[] obj = new string[7]
								{
									"module=LLM; event=speech_contract_invalid; expected_language=",
									SpeechLanguages.ToCode(speechLanguage),
									"; plain_text=",
									null,
									null,
									null,
									null
								};
								isEnabled = parsedReply.UsedPlainTextCompatibility;
								obj[3] = isEnabled.ToString();
								obj[4] = "; narration_present=";
								isEnabled = parsedReply.SpeechText != null;
								obj[5] = isEnabled.ToString();
								obj[6] = "; visible_reply_preserved=true; text_logged=false";
								log.LogWarning(string.Concat(obj));
							}
							ManualLogSource log2 = _log;
							BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(109, 2, out isEnabled);
							if (isEnabled)
							{
								bepInExInfoLogInterpolatedStringHandler.AppendLiteral("module=LLM; event=conversation_turn_committed; revision=");
								bepInExInfoLogInterpolatedStringHandler.AppendFormatted(saved.Conversation.Revision);
								bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; worldbook_matches=");
								bepInExInfoLogInterpolatedStringHandler.AppendFormatted(context.WorldBookMatchCount);
								bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; coordinated_output_handoff=true");
							}
							log2.LogInfo(bepInExInfoLogInterpolatedStringHandler);
						}
						catch (Exception ex2)
						{
							_globalStatus = "本轮对话已保存；下游气泡输出桥处理失败。";
							ManualLogSource log3 = _log;
							BepInExWarningLogInterpolatedStringHandler bepInExWarningLogInterpolatedStringHandler = new BepInExWarningLogInterpolatedStringHandler(46, 1, out isEnabled);
							if (isEnabled)
							{
								bepInExWarningLogInterpolatedStringHandler.AppendLiteral("module=GameBridge; event=publish_failed; code=");
								bepInExWarningLogInterpolatedStringHandler.AppendFormatted(ex2.GetType().Name);
							}
							log3.LogWarning(bepInExWarningLogInterpolatedStringHandler);
						}
						_view.ClearChatInput();
						SetGlobalStatus(_globalStatus);
					}
				}).ConfigureAwait(continueOnCapturedContext: false);
				CancellationTokenSource memoryOperation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
				CancellationToken memoryToken = memoryOperation.Token;
				_memoryCancellation = memoryOperation;
				_memorySessionId = saved.SessionId;
				_memoryTask = Task.Run(async delegate
				{
					using (memoryOperation)
					{
						await ConsolidateInBackgroundAsync(saved, profile, memoryToken).ConfigureAwait(continueOnCapturedContext: false);
					}
				});
				goto end_IL_0121;
				end_IL_014f:;
			}
			catch (OperationCanceledException)
			{
				await OnUi(delegate
				{
					if (_chatCancellation == operation)
					{
						SetGlobalStatus("已取消等待回复；服务端可能已开始处理，输入内容已保留。");
					}
				}).ConfigureAwait(continueOnCapturedContext: false);
				goto end_IL_0121;
			}
			catch (Exception error)
			{
				if (generation == Volatile.Read(ref _chatGeneration))
				{
					await Report(error, "文字请求；输入内容已保留", null).ConfigureAwait(continueOnCapturedContext: false);
				}
				goto end_IL_0121;
			}
			end_IL_0121:;
		}
		finally
		{
			await OnUi(delegate
			{
				_thinking?.EndThinking();
				if (_chatCancellation == operation)
				{
					_chatBusy = false;
					_chatCommitting = false;
					_chatCancellation = null;
					UpdateBusy();
				}
			}).ConfigureAwait(continueOnCapturedContext: false);
			operation.Dispose();
		}
	}

	private async Task ConsolidateInBackgroundAsync(ActiveConversationSnapshot session, LlmProfileSettings profile, CancellationToken token)
	{
		bool isEnabled;
		try
		{
			ActiveConversationSnapshot activeConversationSnapshot = await _memory.ConsolidateAsync(session, profile, token).ConfigureAwait(continueOnCapturedContext: false);
			if (activeConversationSnapshot.Conversation.Revision != session.Conversation.Revision)
			{
				ManualLogSource log = _log;
				BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(99, 3, out isEnabled);
				if (isEnabled)
				{
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("module=LLM; event=dream_memory_compacted; recent_turns=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(activeConversationSnapshot.Conversation.Turns.Count);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; short_term=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(activeConversationSnapshot.ShortTermMemories.Count);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; long_term=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(activeConversationSnapshot.LongTermMemories.Count);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; text_logged=false");
				}
				log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			string t = ((ex2 is LlmException ex3) ? ex3.Code : ex2.GetType().Name);
			ManualLogSource log2 = _log;
			BepInExWarningLogInterpolatedStringHandler bepInExWarningLogInterpolatedStringHandler = new BepInExWarningLogInterpolatedStringHandler(105, 1, out isEnabled);
			if (isEnabled)
			{
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral("module=LLM; event=dream_memory_deferred; code=");
				bepInExWarningLogInterpolatedStringHandler.AppendFormatted(t);
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral("; source_preserved=true; retry=next_send; text_logged=false");
			}
			log2.LogWarning(bepInExWarningLogInterpolatedStringHandler);
		}
	}

	private void PresentAll(bool clearAllKeys)
	{
		_updating = true;
		try
		{
			_view.ApplyState(new LlmConfigurationViewState
			{
				Profiles = _settings.Profiles.Select((LlmProfileSettings p) => CreateProfileState(p.ProfileId)).ToArray(),
				GlobalStatus = _globalStatus,
				Reply = _reply,
				ActiveSummary = DescribeActive()
			}, clearAllKeys);
		}
		finally
		{
			_updating = false;
		}
	}

	private LlmProfileViewState CreateProfileState(string profileId)
	{
		LlmProfileSettings llmProfileSettings = FindProfile(_settings, profileId);
		LlmProfileDraft llmProfileDraft = (_drafts.TryGetValue(profileId, out LlmProfileDraft value) ? value : ToDraft(llmProfileSettings));
		ReasoningMode mode = ParseMode(llmProfileDraft.ReasoningMode);
		ReasoningDecision reasoningDecision = EvaluateForUi(llmProfileDraft.Model, mode, llmProfileDraft.ReasoningEffort);
		IReadOnlyList<string> models;
		if (!_models.TryGetValue(profileId, out IReadOnlyList<string> value2))
		{
			IReadOnlyList<string> readOnlyList = Array.Empty<string>();
			models = readOnlyList;
		}
		else
		{
			models = value2;
		}
		LlmProfileViewState obj = new LlmProfileViewState
		{
			ProfileId = profileId,
			DisplayName = llmProfileSettings.DisplayName,
			IsTop = (_settings.Profiles[0].ProfileId == profileId),
			HasSavedApiKey = llmProfileSettings.HasApiKey,
			BaseUrl = llmProfileDraft.BaseUrl,
			Protocol = llmProfileDraft.Protocol,
			SelectedModel = llmProfileDraft.Model,
			ReasoningMode = llmProfileDraft.ReasoningMode,
			ReasoningEffort = llmProfileDraft.ReasoningEffort,
			AllowedReasoningEfforts = reasoningDecision.AllowedEfforts,
			KnownReasoningModel = reasoningDecision.KnownModel,
			Models = models,
			Status = (_profileStatus.TryGetValue(profileId, out string value3) ? value3 : (llmProfileSettings.HasApiKey ? "已保存密钥；尚未刷新本次模型列表。" : "尚未保存 API Key。")),
			EffectiveReasoning = reasoningDecision.Description,
			Dirty = _dirty.Contains(profileId)
		};
		return obj;
	}

	private LlmProfileDraft NormalizeReasoningDraft(LlmProfileDraft draft, LlmProfileSettings saved)
	{
		ReasoningMode num = ParseMode(draft.ReasoningMode);
		ReasoningDecision reasoningDecision = EvaluateForUi(draft.Model, ReasoningMode.Omit, draft.ReasoningEffort);
		LlmProfileDraft llmProfileDraft = (_drafts.TryGetValue(draft.ProfileId, out LlmProfileDraft value) ? value : ToDraft(saved));
		bool flag = !string.Equals(llmProfileDraft.Model, draft.Model, StringComparison.Ordinal);
		bool flag2 = num == ReasoningMode.Custom && ParseMode(llmProfileDraft.ReasoningMode) != ReasoningMode.Custom;
		bool flag3 = num == ReasoningMode.Custom && saved.ReasoningMode == ReasoningMode.Custom && string.Equals(saved.ModelId, draft.Model, StringComparison.Ordinal) && reasoningDecision.AllowedEfforts.Contains<string>(saved.CustomReasoningEffort, StringComparer.Ordinal);
		string text = ((!(flag | flag2)) ? (reasoningDecision.AllowedEfforts.Contains<string>(draft.ReasoningEffort, StringComparer.Ordinal) ? draft.ReasoningEffort : (flag3 ? saved.CustomReasoningEffort : reasoningDecision.AllowedEfforts[0])) : (flag3 ? saved.CustomReasoningEffort : reasoningDecision.AllowedEfforts[0]));
		if (text != draft.ReasoningEffort)
		{
			_view.SetReasoningEffort(draft.ProfileId, text);
		}
		return draft with
		{
			ReasoningEffort = text
		};
	}

	private static ReasoningDecision EvaluateForUi(string model, ReasoningMode mode, string effort)
	{
		if (string.IsNullOrWhiteSpace(model))
		{
			return new ReasoningDecision(KnownModel: false, null, ReasoningPolicy.CustomEfforts, "选择模型后：已知推理模型自动使用真实最低档；未知别名自动省略参数。");
		}
		try
		{
			return ReasoningPolicy.Evaluate(model, mode, effort);
		}
		catch (LlmException)
		{
			return ReasoningPolicy.Evaluate(model, ReasoningMode.Omit)with
			{
				Description = "所选档位不适用于此模型，已切换到最低可用档。"
			};
		}
	}

	private string DescribeActive()
	{
		if (_agentReady && _agentSettings.Enabled)
		{
			return "当前：莉莉丝 Agent（独立专属对话；普通聊天记忆保持不变）";
		}
		LlmProfileSettings activeProfile = _settings.ActiveProfile;
		if (!activeProfile.IsConfigured)
		{
			return "当前：" + activeProfile.DisplayName + " 尚未完成；不会自动改用下面的地址。";
		}
		return $"当前：{activeProfile.DisplayName} / {activeProfile.ModelId}（严格使用最上方配置）";
	}

	private void UpdateBusy()
	{
		_view.SetBusy(_connectionBusy, _chatBusy, _busyProfileId);
	}

	private bool IsCurrentChat(long generation, CancellationTokenSource operation)
	{
		if (generation == Volatile.Read(ref _chatGeneration) && _chatCancellation == operation)
		{
			return !operation.IsCancellationRequested;
		}
		return false;
	}

	private void CancelConnection()
	{
		if (_connectionBusy && _connectionCancellation != null && Volatile.Read(ref _stopped) == 0)
		{
			string busyProfileId = _busyProfileId;
			if (busyProfileId != null)
			{
				SetProfileStatus(busyProfileId, "正在取消当前保存或模型检测操作…");
			}
			else
			{
				SetGlobalStatus("正在取消当前配置操作…");
			}
			Cancel(_connectionCancellation);
		}
	}

	private void CancelChat()
	{
		if (_chatBusy && _chatCancellation != null && Volatile.Read(ref _stopped) == 0)
		{
			if (_agentChat)
			{
				Cancel(_chatCancellation);
				_agent.CancelActive();
				SetGlobalStatus("正在取消 Agent；专属对话与已执行工作保留，请在 Codex 中查看。");
				return;
			}
			if (_chatCommitting)
			{
				SetGlobalStatus("回复已经产生，正在保存完整回合；请稍候。");
				return;
			}
			CancellationTokenSource? chatCancellation = _chatCancellation;
			Interlocked.Increment(ref _chatGeneration);
			_chatCancellation = null;
			_chatBusy = false;
			Cancel(chatCancellation);
			SetGlobalStatus("已取消等待回复；迟到回复将被丢弃，输入内容已保留。");
			UpdateBusy();
		}
	}

	private void SetGlobalStatus(string status)
	{
		_globalStatus = status;
		_view.SetGlobalStatus(status);
	}

	private void SetProfileStatus(string profileId, string status)
	{
		_profileStatus[profileId] = status;
		_view.SetProfileStatus(profileId, status);
	}

	private Task Report(Exception error, string action, string? profileId)
	{
		return OnUi(delegate
		{
			string text = ((error is LlmException ex) ? ex.Message : "发生本地错误，请检查配置文件权限或重启后重试。");
			if (profileId == null)
			{
				SetGlobalStatus(action + "：" + text);
			}
			else
			{
				SetProfileStatus(profileId, action + "：" + text);
			}
			ManualLogSource log = _log;
			BepInExWarningLogInterpolatedStringHandler bepInExWarningLogInterpolatedStringHandler = new BepInExWarningLogInterpolatedStringHandler(41, 1, out var isEnabled);
			if (isEnabled)
			{
				bepInExWarningLogInterpolatedStringHandler.AppendLiteral("module=LLM; event=operation_failed; code=");
				bepInExWarningLogInterpolatedStringHandler.AppendFormatted((error is LlmException ex2) ? ex2.Code : error.GetType().Name);
			}
			log.LogWarning(bepInExWarningLogInterpolatedStringHandler);
		});
	}

	private async Task OnUi(Action action)
	{
		if (Volatile.Read(ref _stopped) != 0)
		{
			return;
		}
		try
		{
			await _mainThread.Post(delegate
			{
				if (Volatile.Read(ref _stopped) == 0 && !_view.IsStopped)
				{
					action();
				}
			}, _lifetime.Token).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (OperationCanceledException)
		{
		}
		catch (ObjectDisposedException)
		{
			Dispose();
		}
	}

	private bool TryFindProfile(string profileId, out LlmProfileSettings profile)
	{
		profile = _settings.Profiles.FirstOrDefault((LlmProfileSettings p) => p.ProfileId == profileId);
		return (object)profile != null;
	}

	private static LlmProfileSettings FindProfile(LlmSettings settings, string profileId)
	{
		return settings.Profiles.First((LlmProfileSettings p) => p.ProfileId == profileId);
	}

	private static LlmSettings ReplaceProfile(LlmSettings settings, LlmProfileSettings replacement)
	{
		return settings with
		{
			Profiles = settings.Profiles.Select((LlmProfileSettings p) => (!(p.ProfileId == replacement.ProfileId)) ? p with { } : replacement).ToArray()
		};
	}

	private static LlmProfileDraft ToDraft(LlmProfileSettings profile)
	{
		return new LlmProfileDraft(profile.ProfileId, profile.BaseUrl, "", Protocol(profile.ApiFormat), profile.ModelId, Mode(profile.ReasoningMode), profile.CustomReasoningEffort);
	}

	private static bool EquivalentUrl(string left, string right)
	{
		try
		{
			string a = (string.IsNullOrWhiteSpace(left) ? "" : LlmEndpoint.Normalize(left));
			string b = (string.IsNullOrWhiteSpace(right) ? "" : LlmEndpoint.Normalize(right));
			return string.Equals(a, b, StringComparison.Ordinal);
		}
		catch (LlmException)
		{
			return false;
		}
	}

	private static string Protocol(LlmApiFormat format)
	{
		if (format != LlmApiFormat.Responses)
		{
			return "chat_completions";
		}
		return "responses";
	}

	private static LlmApiFormat ParseProtocol(string format)
	{
		if (!(format == "responses"))
		{
			return LlmApiFormat.ChatCompletions;
		}
		return LlmApiFormat.Responses;
	}

	private static string Mode(ReasoningMode mode)
	{
		return mode switch
		{
			ReasoningMode.Omit => "omit", 
			ReasoningMode.Custom => "custom", 
			_ => "auto", 
		};
	}

	private static ReasoningMode ParseMode(string mode)
	{
		if (!(mode == "omit"))
		{
			if (mode == "custom")
			{
				return ReasoningMode.Custom;
			}
			return ReasoningMode.AutoLowest;
		}
		return ReasoningMode.Omit;
	}

	private static string SafeProfile(string profileId)
	{
		bool flag;
		switch (profileId)
		{
		case "profile-1":
		case "profile-2":
		case "profile-3":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			return "unknown";
		}
		return profileId;
	}

	private static void Cancel(CancellationTokenSource? source)
	{
		try
		{
			source?.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _stopped, 1) == 0)
		{
			Interlocked.Increment(ref _chatGeneration);
			Cancel(_lifetime);
			Cancel(_connectionCancellation);
			Cancel(_chatCancellation);
			Cancel(_memoryCancellation);
			_agent.Dispose();
			_client.Dispose();
			_lifetime.Dispose();
		}
	}
}
