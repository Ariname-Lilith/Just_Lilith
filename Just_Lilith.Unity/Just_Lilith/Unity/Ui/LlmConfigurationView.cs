using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using BepInEx.Logging;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using Just_Lilith.Core.Contracts;
using Just_Lilith.Core.Llm;
using Just_Lilith.Core.Speech;
using Just_Lilith.Unity.Game;
using Just_Lilith.Unity.Speech;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Just_Lilith.Unity.Ui;

public sealed class LlmConfigurationView : MonoBehaviour
{
	private sealed class ProfileWidgets
	{
		public string ProfileId { get; }

		public GameObject Root { get; }

		public Image RootImage { get; }

		public Outline Border { get; }

		public Text Title { get; }

		public Text Status { get; }

		public InputField Url { get; }

		public InputField ApiKey { get; }

		public InputField Search { get; }

		public Button ProtocolButton { get; }

		public Text ProtocolButtonText { get; }

		public Button ModelButton { get; }

		public Text ModelButtonText { get; }

		public Button ModelUp { get; }

		public Button ModelDown { get; }

		public Button ReasonMode { get; }

		public Text ReasonModeText { get; }

		public Button ReasonEffort { get; }

		public Text ReasonEffortText { get; }

		public Button EffortUp { get; }

		public Button EffortDown { get; }

		public Text ReasonKnown { get; }

		public Text ReasonDescription { get; }

		public Button MoveUp { get; }

		public Button MoveDown { get; }

		public Button Save { get; }

		public Text SaveButtonText { get; }

		public Button Refresh { get; }

		public Button Cancel { get; }

		public string Protocol { get; set; } = "chat_completions";

		public string Model { get; set; } = "";

		public string[] Models { get; set; } = Array.Empty<string>();

		public string ReasoningMode { get; set; } = "auto";

		public string ReasoningEffort { get; set; } = "low";

		public string[] AllowedEfforts { get; set; } = Array.Empty<string>();

		public int Order { get; set; }

		public bool IsTop { get; set; }

		public bool Dirty { get; set; }

		public ProfileWidgets(string profileId, GameObject root, Image rootImage, Outline border, Text title, Text status, InputField url, InputField apiKey, InputField search, Button protocolButton, Button modelButton, Button modelUp, Button modelDown, Button reasonMode, Button reasonEffort, Button effortUp, Button effortDown, Text reasonKnown, Text reasonDescription, Button moveUp, Button moveDown, Button save, Button refresh, Button cancel)
		{
			ProfileId = profileId;
			Root = root;
			RootImage = rootImage;
			Border = border;
			Title = title;
			Status = status;
			Url = url;
			ApiKey = apiKey;
			Search = search;
			ProtocolButton = protocolButton;
			ProtocolButtonText = protocolButton.GetComponentInChildren<Text>();
			ModelButton = modelButton;
			ModelButtonText = modelButton.GetComponentInChildren<Text>();
			ModelUp = modelUp;
			ModelDown = modelDown;
			ReasonMode = reasonMode;
			ReasonModeText = reasonMode.GetComponentInChildren<Text>();
			ReasonEffort = reasonEffort;
			ReasonEffortText = reasonEffort.GetComponentInChildren<Text>();
			EffortUp = effortUp;
			EffortDown = effortDown;
			ReasonKnown = reasonKnown;
			ReasonDescription = reasonDescription;
			MoveUp = moveUp;
			MoveDown = moveDown;
			Save = save;
			SaveButtonText = save.GetComponentInChildren<Text>();
			Refresh = refresh;
			Cancel = cancel;
		}
	}

	private sealed class ProfileSelectorWidgets
	{
		public Button Button { get; }

		public Text Label { get; }

		public string DisplayName { get; set; } = "";

		public int Order { get; set; }

		public bool IsTop { get; set; }

		public bool Dirty { get; set; }

		public ProfileSelectorWidgets(Button button, Text label)
		{
			Button = button;
			Label = label;
		}
	}

	private NativeLilithConversationRows? _agentRows;

	private NativeLilithConversationRows? _agentConfigurationRows;

	private GameObject? _agentChoiceMenu;

	private float _nextAgentRefresh;

	private const int OtherTabValue = 6;

	private const float ReferenceWidth = 1920f;

	private const float ReferenceHeight = 1080f;

	private readonly Dictionary<string, ProfileWidgets> _rows = new Dictionary<string, ProfileWidgets>(StringComparer.Ordinal);

	private readonly Dictionary<string, ProfileSelectorWidgets> _profileSelectors = new Dictionary<string, ProfileSelectorWidgets>(StringComparer.Ordinal);

	private readonly List<InputField> _allInputs = new List<InputField>();

	private readonly GameKeyboardInputBridge _keyboard = new GameKeyboardInputBridge();

	private readonly ChatHotkeyController _chatHotkey = new ChatHotkeyController();

	private readonly F7PressDeduplicator _f7Deduplicator = new F7PressDeduplicator();

	private LlmConfigurationCallbacks _callbacks = new LlmConfigurationCallbacks();

	private LlmGameBridge? _gameBridge;

	private Font? _font;

	private bool _ownsFont;

	private GameObject? _uiRoot;

	private GameObject? _settingsPanel;

	private RectTransform? _settingsContent;

	private GameObject? _chatPanel;

	private GameObject? _gameSettingsTab;

	private Button? _gameSettingsTabButton;

	private object? _gameSettingsTabTitle;

	private Image? _gameSettingsTabImage;

	private Sprite? _gameSettingsTabSelectedSprite;

	private Sprite? _gameSettingsTabUnselectedSprite;

	private Text? _globalStatus;

	private Text? _activeSummary;

	private Text? _reply;

	private InputField? _chatInput;

	private Button? _chatSend;

	private object? _traySettingsView;

	private RuntimeValueAccessor? _trayVisible;

	private RuntimeValueAccessor? _trayTab;

	private RuntimeValueAccessor? _trayItemRoot;

	private RuntimeValueAccessor? _trayTabPrefab;

	private RuntimeValueAccessor? _trayTabContainer;

	private RuntimeValueAccessor? _trayTabItems;

	private Transform? _settingsPanelHome;

	private bool _apiTabSelected;

	private string? _visibleProfileId;

	private int _nativeSkinRootId;

	private int _nativeChatSkinRootId;

	private float _nextTrayScan;

	private bool _connectionBusy;

	private bool _chatBusy;

	private string? _busyProfileId;

	private bool _suppress;

	private bool _initialized;

	private Exception? _awakeError;

	private int _stopRequested;

	private bool _cleaned;

	private bool _applicationQuitting;

	private bool _runInBackgroundCaptured;

	private bool _previousRunInBackground;

	private int _lastCompositionFrame = -100;

	private int _lastChatSubmitFrame = -1;

	private float _chatFocusPendingUntil;

	private string _lastGlobalStatus = "正在载入配置…";

	private string _lastActiveSummary = "顶部配置尚未完成。";

	private string _lastReply = "";

	private ManualLogSource? _log;

	private string _lastTrayDiagnostic = "";

	private int _trayCandidateCount;

	private static readonly Color RowBackground = new Color(0.075f, 0.075f, 0.075f, 1f);

	private static readonly Color ActiveRowBackground = new Color(0.13f, 0.13f, 0.13f, 1f);

	private static readonly Color DirtyRowBackground = new Color(0.18f, 0.18f, 0.18f, 1f);

	private static readonly Color FieldBackground = new Color(0.62f, 0.62f, 0.6f, 1f);

	private static readonly Color ButtonBackground = new Color(0.22f, 0.22f, 0.21f, 1f);

	private static readonly Color InputInk = new Color(0.08f, 0.08f, 0.08f, 1f);

	private static readonly Color InputPlaceholder = new Color(0.34f, 0.34f, 0.33f, 1f);

	private static readonly Color Muted = new Color(0.65f, 0.65f, 0.63f, 1f);

	private static readonly Color Accent = new Color(0.92f, 0.92f, 0.9f, 1f);

	private static readonly Color Good = new Color(0.76f, 0.76f, 0.74f, 1f);

	private static readonly Color Warning = new Color(0.88f, 0.88f, 0.85f, 1f);

	private static readonly Color Bad = new Color(0.96f, 0.96f, 0.94f, 1f);

	private const int NativeLilithTabValue = 4;

	private const int JourneysPerPage = 7;

	private NativeLilithConversationRows? _lilithRows;

	private Transform? _lilithItemRoot;

	private GameObject? _journeyDialog;

	private RectTransform? _journeyDragHeader;

	private bool _journeyDragging;

	private Vector2 _journeyDragStartPointer;

	private Vector2 _journeyDragStartPosition;

	private InputField? _journeyRenameInput;

	private Text? _journeyDialogStatus;

	private Text? _journeyPageText;

	private readonly List<Button> _journeyListButtons = new List<Button>();

	private IReadOnlyList<ActiveConversationInfo> _journeySessions = Array.Empty<ActiveConversationInfo>();

	private string? _journeySelectedId;

	private string? _journeyDeleteConfirmationId;

	private string _journeyActiveName = "新建幻境1";

	private string _journeyDiagnostic = "";

	private int _journeyPage;

	private const int NativeSpeechLanguageTabValue = 3;

	private MainThreadSpeechController? _speechController;

	private SpeechUiCallbacks _speechCallbacks = new SpeechUiCallbacks();

	private NativeSpeechLanguageRows? _speechRows;

	private SpeechSettings _lastSpeechSettings = new SpeechSettings();

	private TtsServiceState _lastServiceState = new TtsServiceState(TtsServicePhase.Off, "TTS 服务已关闭");

	private string _lastSpeechStatus = "";

	private string _lastSpeechRowsDiagnostic = "";

	private bool _speechLoaded;

	[HideFromIl2Cpp]
	public bool IsStopped
	{
		get
		{
			if (Volatile.Read(ref _stopRequested) == 0)
			{
				return _cleaned;
			}
			return true;
		}
	}

	private void MaintainNativeAgent()
	{
		if (_applicationQuitting || IsStopped)
		{
			return;
		}
		try
		{
			if (!_apiTabSelected && _traySettingsView != null && !(_uiRoot == null))
			{
				object obj = _trayVisible?.GetValue(_traySettingsView);
				if (obj is bool && (bool)obj && Convert.ToInt32(_trayTab?.GetValue(_traySettingsView)) == 4 && _trayItemRoot?.GetValue(_traySettingsView) is Transform transform && !(transform == null) && transform.gameObject.activeInHierarchy)
				{
					if (_agentRows != null && !_agentRows.IsValidFor(transform))
					{
						DetachAgentRows();
					}
					if (_agentConfigurationRows != null && !_agentConfigurationRows.IsValidFor(transform))
					{
						DetachAgentRows();
					}
					string diagnostic;
					if (_agentRows == null)
					{
						if (!NativeLilithConversationRows.TryCreate(transform, _uiRoot.transform, delegate
						{
							RunUiCallback("AgentPersona", delegate
							{
								_callbacks.AgentPersonaRequested?.Invoke();
							});
						}, delegate
						{
							RunUiCallback("AgentToggle", delegate
							{
								_callbacks.AgentToggleRequested?.Invoke();
							});
						}, delegate
						{
							RunUiCallback("AgentCancel", delegate
							{
								_callbacks.ChatCancelRequested?.Invoke();
							});
						}, new float[3] { 0.32f, 0.5f, 0.18f }, anchorBelowWardrobe: true, 0, out NativeLilithConversationRows result, out diagnostic))
						{
							return;
						}
						result.ConfigureCircularColumn(2, "■", Color.black, new Color(0.42f, 0.42f, 0.42f, 0.8f));
						_agentRows = result;
						_nextAgentRefresh = 0f;
					}
					if (_agentConfigurationRows == null)
					{
						if (!NativeLilithConversationRows.TryCreate(transform, _uiRoot.transform, delegate
						{
							RunUiCallback("AgentModelMenu", OpenAgentModelMenu);
						}, delegate
						{
							RunUiCallback("AgentReasoningMenu", OpenAgentReasoningMenu);
						}, delegate
						{
							RunUiCallback("AgentSettings", delegate
							{
								_callbacks.AgentSettingsRequested?.Invoke();
							});
						}, new float[3] { 0.41f, 0.41f, 0.18f }, anchorBelowWardrobe: true, 1, out NativeLilithConversationRows result2, out diagnostic))
						{
							return;
						}
						result2.TryUseGameLanguageSelectorStyle(0, 1);
						result2.ConfigureCircularColumn(2, "⚙", new Color(0.55f, 0.55f, 0.55f, 1f), new Color(0.34f, 0.34f, 0.34f, 0.75f));
						_agentConfigurationRows = result2;
						_nextAgentRefresh = 0f;
					}
					_agentRows.MaintainPlacement();
					_agentConfigurationRows.MaintainPlacement();
					if (!(Time.unscaledTime < _nextAgentRefresh))
					{
						_nextAgentRefresh = Time.unscaledTime + 0.15f;
						AgentUiState agentUiState = _callbacks.AgentStatusRequested?.Invoke();
						if (!(agentUiState == null))
						{
							string text = ((!agentUiState.Enabled) ? "Agent：关" : (agentUiState.Busy ? "Agent：响应中" : "Agent：开"));
							_agentRows.SetCaptions("Agent Persona", text, "■");
							_agentRows.SetInteractivity(!_connectionBusy && !_chatBusy, !_connectionBusy && (!_chatBusy || agentUiState.Busy), agentUiState.Busy);
							_agentConfigurationRows.SetCaptions("模型：" + CompactAgentModel(agentUiState.Model), "推理：" + agentUiState.ReasoningEffort, "⚙");
							_agentConfigurationRows.SetInteractivity(!_connectionBusy && !_chatBusy, !_connectionBusy && !_chatBusy, !_connectionBusy && !_chatBusy);
						}
					}
					return;
				}
			}
			DetachAgentRows();
		}
		catch (Exception error)
		{
			DetachAgentRows();
			SafeLogCallbackError("MaintainNativeAgent", error);
		}
	}

	[HideFromIl2Cpp]
	private void OpenAgentModelMenu()
	{
		AgentUiState agentUiState = _callbacks.AgentStatusRequested?.Invoke();
		if (!(agentUiState == null) && !_chatBusy && !_connectionBusy)
		{
			ShowAgentChoiceMenu("Agent 模型", agentUiState.ModelOptions, agentUiState.Model, 0, delegate(string value)
			{
				_callbacks.AgentModelSelectedRequested?.Invoke(value);
			});
		}
	}

	[HideFromIl2Cpp]
	private void OpenAgentReasoningMenu()
	{
		AgentUiState agentUiState = _callbacks.AgentStatusRequested?.Invoke();
		if (!(agentUiState == null) && !_chatBusy && !_connectionBusy)
		{
			ShowAgentChoiceMenu("Agent 推理强度", agentUiState.ReasoningOptions, agentUiState.ReasoningEffort, 1, delegate(string value)
			{
				_callbacks.AgentReasoningSelectedRequested?.Invoke(value);
			});
		}
	}

	[HideFromIl2Cpp]
	private void ShowAgentChoiceMenu(string title, IReadOnlyList<string> choices, string selected, int column, Action<string> choose)
	{
		if (_uiRoot == null || _agentConfigurationRows == null)
		{
			return;
		}
		RectTransform component = _uiRoot.GetComponent<RectTransform>();
		if ((object)component == null || !_agentConfigurationRows.TryGetColumnRect(component, column, out var bounds))
		{
			return;
		}
		CloseAgentChoiceMenu();
		GameObject gameObject = (_agentChoiceMenu = RectObject("Just_Lilith.Agent.ChoiceOverlay", _uiRoot.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero));
		Image image = AddImage(gameObject, Color.clear);
		image.raycastTarget = true;
		Button button = gameObject.AddComponent<Button>();
		button.targetGraphic = image;
		button.onClick.AddListener((Action)CloseAgentChoiceMenu);
		string[] array = choices.Take(8).ToArray();
		float num = Mathf.Max(210f, bounds.width + 54f);
		float num2 = 36f + (float)array.Length * 34f + 8f;
		GameObject gameObject2 = RectObject("ChoicePanel", gameObject.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(num, num2), Vector2.zero);
		RectTransform component2 = gameObject2.GetComponent<RectTransform>();
		component2.pivot = new Vector2(0f, 1f);
		float num3 = bounds.yMin - 5f;
		if (num3 - num2 < component.rect.yMin + 8f)
		{
			num3 = bounds.yMax + num2 + 5f;
		}
		component2.anchoredPosition = new Vector2(Mathf.Clamp(bounds.xMin, component.rect.xMin + 8f, component.rect.xMax - num - 8f), num3);
		AddImage(gameObject2, new Color(0.1f, 0.1f, 0.1f, 0.98f)).raycastTarget = true;
		Outline outline = gameObject2.AddComponent<Outline>();
		outline.effectColor = new Color(0.65f, 0.65f, 0.65f, 1f);
		outline.effectDistance = new Vector2(1f, -1f);
		CreateText(gameObject2.transform, "Title", title, 14, TextAnchor.MiddleLeft, new Vector2(10f, -4f), new Vector2(num - 20f, 28f), FontStyle.Bold);
		for (int i = 0; i < array.Length; i++)
		{
			string value = array[i];
			string label = (string.Equals(value, selected, StringComparison.Ordinal) ? "● " : "  ") + AgentChoiceLabel(value);
			CreateButton(gameObject2.transform, "Choice" + i, label, new Vector2(8f, -34 - i * 34), new Vector2(num - 16f, 30f), delegate
			{
				choose(value);
				CloseAgentChoiceMenu();
				_nextAgentRefresh = 0f;
			});
		}
		gameObject2.transform.SetAsLastSibling();
	}

	private static string AgentChoiceLabel(string value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value;
		}
		return "-";
	}

	private static string CompactAgentModel(string model)
	{
		string text = AgentChoiceLabel(model);
		if (text.Length <= 15)
		{
			return text;
		}
		return text.Substring(0, 14) + "…";
	}

	private void CloseAgentChoiceMenu()
	{
		if (_agentChoiceMenu != null)
		{
			_agentChoiceMenu.SetActive(value: false);
			UnityEngine.Object.Destroy(_agentChoiceMenu);
		}
		_agentChoiceMenu = null;
	}

	private void DetachAgentRows()
	{
		CloseAgentChoiceMenu();
		NativeLilithConversationRows? agentRows = _agentRows;
		_agentRows = null;
		agentRows?.Dispose();
		NativeLilithConversationRows? agentConfigurationRows = _agentConfigurationRows;
		_agentConfigurationRows = null;
		agentConfigurationRows?.Dispose();
	}

	public LlmConfigurationView(IntPtr pointer)
		: base(pointer)
	{
	}

	public void Awake()
	{
		try
		{
			EnableBackgroundKeyboardPolling();
			BuildUi();
		}
		catch (Exception awakeError)
		{
			_awakeError = awakeError;
			RequestStop();
		}
	}

	[HideFromIl2Cpp]
	internal void AttachGameBridge(LlmGameBridge bridge)
	{
		_gameBridge = bridge;
	}

	[HideFromIl2Cpp]
	internal void AttachLogger(ManualLogSource logger)
	{
		_log = logger;
		_chatHotkey.AttachLogger(logger);
	}

	[HideFromIl2Cpp]
	public void Initialize(LlmConfigurationCallbacks callbacks)
	{
		ArgumentNullException.ThrowIfNull(callbacks, "callbacks");
		if (_initialized)
		{
			throw new InvalidOperationException("LLM configuration view is already initialized.");
		}
		if (_awakeError != null)
		{
			throw new InvalidOperationException("LLM configuration UI failed during Awake.", _awakeError);
		}
		BuildUi();
		_callbacks = callbacks;
		_initialized = true;
	}

	[HideFromIl2Cpp]
	public void ApplyState(LlmConfigurationViewState state, bool clearAllKeys)
	{
		if (IsStopped)
		{
			return;
		}
		_suppress = true;
		try
		{
			for (int i = 0; i < state.Profiles.Count; i++)
			{
				LlmProfileViewState llmProfileViewState = state.Profiles[i];
				ProfileWidgets orCreateRow = GetOrCreateRow(llmProfileViewState.ProfileId);
				SetRowPosition(orCreateRow, i);
				ApplyProfile(orCreateRow, llmProfileViewState, clearAllKeys);
			}
			RefreshProfileVisibility();
			_lastGlobalStatus = state.GlobalStatus;
			_lastActiveSummary = state.ActiveSummary;
			_lastReply = state.Reply;
			_globalStatus.text = state.GlobalStatus;
			_globalStatus.color = (LooksLikeError(state.GlobalStatus) ? Bad : Accent);
			_activeSummary.text = state.ActiveSummary;
			_reply.text = state.Reply;
		}
		finally
		{
			_suppress = false;
		}
		RefreshInteractivity();
	}

	[HideFromIl2Cpp]
	public void ApplyProfileState(LlmProfileViewState state, bool clearKey)
	{
		if (!IsStopped)
		{
			ProfileWidgets orCreateRow = GetOrCreateRow(state.ProfileId);
			_suppress = true;
			try
			{
				ApplyProfile(orCreateRow, state, clearKey);
				RefreshProfileVisibility();
			}
			finally
			{
				_suppress = false;
			}
			RefreshInteractivity();
		}
	}

	[HideFromIl2Cpp]
	public void SetBusy(bool connectionBusy, bool chatBusy, string? busyProfileId)
	{
		_connectionBusy = connectionBusy;
		_chatBusy = chatBusy;
		_busyProfileId = busyProfileId;
		RefreshInteractivity();
	}

	[HideFromIl2Cpp]
	public void SetProfileStatus(string profileId, string status)
	{
		if (_rows.TryGetValue(profileId, out ProfileWidgets value))
		{
			value.Status.text = status;
			value.Status.color = StatusColor(status, value.Dirty);
		}
	}

	[HideFromIl2Cpp]
	public void SetGlobalStatus(string status)
	{
		_lastGlobalStatus = status;
		if (_globalStatus != null)
		{
			_globalStatus.text = status;
			_globalStatus.color = (LooksLikeError(status) ? Bad : Accent);
		}
	}

	[HideFromIl2Cpp]
	public void SetActiveSummary(string summary)
	{
		_lastActiveSummary = summary;
		if (_activeSummary != null)
		{
			_activeSummary.text = summary;
		}
	}

	[HideFromIl2Cpp]
	public void SetReasoningEffort(string profileId, string effort)
	{
		if (!_rows.TryGetValue(profileId, out ProfileWidgets value))
		{
			return;
		}
		_suppress = true;
		try
		{
			value.ReasoningEffort = effort;
			UpdateReasoningCaptions(value);
		}
		finally
		{
			_suppress = false;
		}
	}

	[HideFromIl2Cpp]
	public void SetReply(string text)
	{
		_lastReply = text;
		if (_reply != null)
		{
			_reply.text = text;
		}
	}

	[HideFromIl2Cpp]
	public void ClearChatInput()
	{
		if (_chatInput != null)
		{
			_chatInput.text = "";
		}
		RefreshInteractivity();
	}

	[HideFromIl2Cpp]
	public void RequestStop()
	{
		Interlocked.Exchange(ref _stopRequested, 1);
	}

	public void Update()
	{
		try
		{
			if (!_applicationQuitting)
			{
				if (Volatile.Read(ref _stopRequested) != 0)
				{
					CleanupOwnedUi();
					UnityEngine.Object.Destroy(this);
					return;
				}
				_gameBridge?.Update();
				_speechController?.Update();
				MaintainNativeApiTab();
				MaintainNativeLilithSpeech();
				MaintainNativeAgent();
				MaintainNativeLilithConversations();
				MaintainJourneyDialogDrag();
				HandleKeyboard();
			}
		}
		catch (Exception error)
		{
			SafeLogCallbackError("Update", error);
			if (!_applicationQuitting)
			{
				try
				{
					InvalidateTraySettings();
				}
				catch
				{
				}
				_nextTrayScan = Time.unscaledTime + 1f;
			}
		}
	}

	public void OnApplicationQuit()
	{
		_applicationQuitting = true;
		try
		{
			_callbacks.Stopped?.Invoke();
		}
		catch
		{
		}
		_speechController?.RequestStop();
		RequestStop();
	}

	public void OnDestroy()
	{
		try
		{
			CleanupOwnedUi();
		}
		catch (Exception error)
		{
			SafeLogCallbackError("OnDestroy", error);
		}
	}

	private void CleanupOwnedUi()
	{
		if (_cleaned)
		{
			return;
		}
		_cleaned = true;
		_speechController?.ShutdownFromMainThread(_applicationQuitting);
		if (_applicationQuitting)
		{
			_rows.Clear();
			_profileSelectors.Clear();
			_allInputs.Clear();
			ForgetLilithConversationRowsOnQuit();
			_agentRows = null;
			_agentConfigurationRows = null;
			return;
		}
		DetachLilithSpeechRows();
		DetachLilithConversationRows();
		DetachAgentRows();
		RestoreBackgroundKeyboardPolling();
		try
		{
			_chatHotkey.Dispose();
		}
		catch
		{
		}
		try
		{
			_keyboard.SetNeeded(needed: false);
		}
		catch
		{
		}
		try
		{
			ClearOwnSelection();
		}
		catch
		{
		}
		try
		{
			_callbacks.Stopped?.Invoke();
		}
		catch
		{
		}
		try
		{
			RestoreNativeSettingsView();
		}
		catch
		{
		}
		if (_gameSettingsTab != null)
		{
			try
			{
				_gameSettingsTabButton?.onClick.RemoveAllListeners();
			}
			catch
			{
			}
			try
			{
				_gameSettingsTab.SetActive(value: false);
			}
			catch
			{
			}
			try
			{
				UnityEngine.Object.Destroy(_gameSettingsTab);
			}
			catch
			{
			}
		}
		if (_uiRoot != null)
		{
			try
			{
				_uiRoot.SetActive(value: false);
			}
			catch
			{
			}
			try
			{
				UnityEngine.Object.Destroy(_uiRoot);
			}
			catch
			{
			}
		}
		if (_font != null && _ownsFont)
		{
			try
			{
				UnityEngine.Object.Destroy(_font);
			}
			catch
			{
			}
		}
		_rows.Clear();
		_profileSelectors.Clear();
		_allInputs.Clear();
	}

	private void EnableBackgroundKeyboardPolling()
	{
		if (!_runInBackgroundCaptured)
		{
			_previousRunInBackground = Application.runInBackground;
			_runInBackgroundCaptured = true;
			Application.runInBackground = true;
		}
	}

	private void RestoreBackgroundKeyboardPolling()
	{
		if (!_runInBackgroundCaptured)
		{
			return;
		}
		try
		{
			Application.runInBackground = _previousRunInBackground;
		}
		finally
		{
			_runInBackgroundCaptured = false;
		}
	}

	private void BuildUi()
	{
		if (_uiRoot != null)
		{
			return;
		}
		_font = FindLoadedGameFont() ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
		_ownsFont = false;
		if (_font == null)
		{
			throw new InvalidOperationException("No usable uGUI font is loaded.");
		}
		_uiRoot = new GameObject("Just_Lilith.UI.Root", Il2CppType.Of<RectTransform>());
		UnityEngine.Object.DontDestroyOnLoad(_uiRoot);
		Canvas canvas = _uiRoot.AddComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = 32000;
		CanvasScaler canvasScaler = _uiRoot.AddComponent<CanvasScaler>();
		canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		canvasScaler.referenceResolution = new Vector2(1920f, 1080f);
		canvasScaler.matchWidthOrHeight = 0.5f;
		_uiRoot.AddComponent<GraphicRaycaster>();
		_settingsPanelHome = _uiRoot.transform;
		_settingsPanel = RectObject("NativeApiSettingsPage", _uiRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(436f, 390f), Vector2.zero);
		AddImage(_settingsPanel, new Color(0.07f, 0.07f, 0.07f, 0.94f));
		GameObject gameObject = RectObject("Viewport", _settingsPanel.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
		RectTransform component = gameObject.GetComponent<RectTransform>();
		component.offsetMin = Vector2.zero;
		component.offsetMax = Vector2.zero;
		gameObject.AddComponent<RectMask2D>();
		GameObject gameObject2 = new GameObject("Content", Il2CppType.Of<RectTransform>());
		gameObject2.layer = _settingsPanel.layer;
		gameObject2.transform.SetParent(gameObject.transform, worldPositionStays: false);
		_settingsContent = gameObject2.GetComponent<RectTransform>();
		_settingsContent.anchorMin = new Vector2(0.5f, 1f);
		_settingsContent.anchorMax = new Vector2(0.5f, 1f);
		_settingsContent.pivot = new Vector2(0.5f, 1f);
		_settingsContent.anchoredPosition = Vector2.zero;
		_settingsContent.sizeDelta = new Vector2(412f, 390f);
		CreateText(_settingsContent, "Title", "API 设置", 18, TextAnchor.MiddleLeft, new Vector2(8f, -2f), new Vector2(260f, 28f), FontStyle.Bold);
		_activeSummary = CreateText(_settingsContent, "ActiveSummary", "顶部配置尚未完成。", 13, TextAnchor.MiddleLeft, new Vector2(8f, -65f), new Vector2(396f, 22f), FontStyle.Normal, Muted);
		_globalStatus = CreateText(_settingsContent, "GlobalStatus", "正在载入配置…", 12, TextAnchor.UpperLeft, new Vector2(8f, -354f), new Vector2(396f, 32f), FontStyle.Normal, Muted);
		_globalStatus.horizontalOverflow = HorizontalWrapMode.Wrap;
		_reply = CreateText(_settingsContent, "ReplyState", "", 1, TextAnchor.UpperLeft, Vector2.zero, Vector2.one);
		_reply.gameObject.SetActive(value: false);
		_settingsPanel.SetActive(value: false);
		_chatPanel = RectObject("ChatPanel", _uiRoot.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(780f, 54f), new Vector2(0f, 30f));
		AddImage(_chatPanel, Color.clear).raycastTarget = false;
		_chatInput = CreateInput(_chatPanel.transform, "ChatInput", "输入文字；Enter 发送（输入法组词时不发送）", new Vector2(0f, -3f), new Vector2(650f, 48f), password: false, 65536);
		_chatSend = CreateButton(_chatPanel.transform, "SendChat", "发送", new Vector2(658f, -3f), new Vector2(122f, 48f), delegate
		{
			RequestChatSubmit("pointer");
		});
		_chatInput.onValueChanged.AddListener((Action<string>)delegate
		{
			RunUiCallback("ChatInputChanged", RefreshInteractivity);
		});
		_chatInput.onSubmit.AddListener((Action<string>)delegate
		{
			RunUiCallback("ChatInputSubmit", delegate
			{
				RequestChatSubmit("input_submit");
			});
		});
		_chatPanel.SetActive(value: false);
	}

	[HideFromIl2Cpp]
	private static Font? FindLoadedGameFont()
	{
		try
		{
			foreach (Text item in UnityEngine.Object.FindObjectsOfType<Text>(includeInactive: true))
			{
				if (item != null && item.font != null)
				{
					return item.font;
				}
			}
		}
		catch
		{
		}
		return null;
	}

	[HideFromIl2Cpp]
	private ProfileWidgets GetOrCreateRow(string profileId)
	{
		if (_rows.TryGetValue(profileId, out ProfileWidgets value))
		{
			return value;
		}
		Button button = CreateButton(_settingsContent, "ProfileSelector-" + profileId, profileId, new Vector2(8f, -32f), new Vector2(128f, 29f), delegate
		{
			SelectVisibleProfile(profileId);
		});
		ProfileSelectorWidgets value2 = new ProfileSelectorWidgets(button, button.GetComponentInChildren<Text>());
		_profileSelectors.Add(profileId, value2);
		if (_visibleProfileId == null)
		{
			_visibleProfileId = profileId;
		}
		_nativeSkinRootId = 0;
		GameObject gameObject = RectObject("Profile-" + profileId, _settingsContent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(412f, 258f), new Vector2(0f, -91f));
		Image rootImage = AddImage(gameObject, RowBackground);
		Outline outline = gameObject.AddComponent<Outline>();
		outline.effectColor = new Color(0.02f, 0.02f, 0.02f, 1f);
		outline.effectDistance = new Vector2(1f, -1f);
		Text title = CreateText(gameObject.transform, "Title", profileId, 15, TextAnchor.MiddleLeft, new Vector2(8f, -4f), new Vector2(316f, 28f), FontStyle.Bold);
		Button moveUp = CreateButton(gameObject.transform, "MoveUp", "←", new Vector2(330f, -3f), new Vector2(34f, 29f), delegate
		{
			_callbacks.MoveProfileRequested?.Invoke(profileId, -1);
		});
		Button moveDown = CreateButton(gameObject.transform, "MoveDown", "→", new Vector2(368f, -3f), new Vector2(34f, 29f), delegate
		{
			_callbacks.MoveProfileRequested?.Invoke(profileId, 1);
		});
		Text text = CreateText(gameObject.transform, "Status", "尚未载入", 10, TextAnchor.MiddleLeft, new Vector2(8f, -30f), new Vector2(394f, 18f), FontStyle.Normal, Muted);
		text.horizontalOverflow = HorizontalWrapMode.Wrap;
		Button save = CreateButton(gameObject.transform, "Save", "保存并检测", new Vector2(8f, -50f), new Vector2(154f, 26f), delegate
		{
			if (_rows.TryGetValue(profileId, out ProfileWidgets value3))
			{
				_callbacks.SaveProfileRequested?.Invoke(Gather(value3));
			}
		});
		Button refresh = CreateButton(gameObject.transform, "Refresh", "刷新", new Vector2(166f, -50f), new Vector2(108f, 26f), delegate
		{
			_callbacks.RefreshProfileRequested?.Invoke(profileId);
		});
		Button cancel = CreateButton(gameObject.transform, "Cancel", "取消", new Vector2(278f, -50f), new Vector2(124f, 26f), delegate
		{
			_callbacks.CancelConnectionRequested?.Invoke();
		});
		CreateText(gameObject.transform, "UrlLabel", "URL", 12, TextAnchor.MiddleLeft, new Vector2(8f, -80f), new Vector2(38f, 28f), FontStyle.Normal, Muted);
		InputField inputField = CreateInput(gameObject.transform, "Url", "https://HOST/v1（本机可 http）", new Vector2(48f, -80f), new Vector2(354f, 28f), password: false, 2048);
		CreateText(gameObject.transform, "KeyLabel", "API", 12, TextAnchor.MiddleLeft, new Vector2(8f, -112f), new Vector2(38f, 28f), FontStyle.Normal, Muted);
		InputField inputField2 = CreateInput(gameObject.transform, "ApiKey", "输入 API Key", new Vector2(48f, -112f), new Vector2(354f, 28f), password: true, 8192);
		Button protocolButton = CreateButton(gameObject.transform, "Protocol", "Chat Completions", new Vector2(8f, -176f), new Vector2(132f, 28f), delegate
		{
			if (_rows.TryGetValue(profileId, out ProfileWidgets value3))
			{
				value3.Protocol = ((value3.Protocol == "responses") ? "chat_completions" : "responses");
				UpdateProtocolCaption(value3);
				NotifyDraft(value3);
			}
		});
		CreateText(gameObject.transform, "SearchLabel", "模型", 12, TextAnchor.MiddleLeft, new Vector2(8f, -144f), new Vector2(38f, 28f), FontStyle.Normal, Muted);
		InputField inputField3 = CreateInput(gameObject.transform, "Search", "搜索", new Vector2(48f, -144f), new Vector2(104f, 28f), password: false, 256);
		Button modelButton = CreateButton(gameObject.transform, "Model", "尚未检测模型", new Vector2(156f, -144f), new Vector2(176f, 28f), delegate
		{
			CycleModel(profileId, 1);
		});
		Button modelUp = CreateButton(gameObject.transform, "ModelUp", "◀", new Vector2(336f, -144f), new Vector2(32f, 28f), delegate
		{
			CycleModel(profileId, -1);
		});
		Button modelDown = CreateButton(gameObject.transform, "ModelDown", "▶", new Vector2(372f, -144f), new Vector2(30f, 28f), delegate
		{
			CycleModel(profileId, 1);
		});
		Button reasonMode = CreateButton(gameObject.transform, "ReasonMode", "自动最低", new Vector2(144f, -176f), new Vector2(116f, 28f), delegate
		{
			CycleReasonMode(profileId);
		});
		Button reasonEffort = CreateButton(gameObject.transform, "ReasonEffort", "low", new Vector2(264f, -176f), new Vector2(68f, 28f), delegate
		{
			CycleReasonEffort(profileId, 1);
		});
		Button effortUp = CreateButton(gameObject.transform, "EffortUp", "◀", new Vector2(336f, -176f), new Vector2(32f, 28f), delegate
		{
			CycleReasonEffort(profileId, -1);
		});
		Button effortDown = CreateButton(gameObject.transform, "EffortDown", "▶", new Vector2(372f, -176f), new Vector2(30f, 28f), delegate
		{
			CycleReasonEffort(profileId, 1);
		});
		Text reasonKnown = CreateText(gameObject.transform, "ReasonKnown", "", 11, TextAnchor.MiddleLeft, new Vector2(8f, -210f), new Vector2(112f, 38f), FontStyle.Normal, Muted);
		Text text2 = CreateText(gameObject.transform, "ReasonDescription", "选择模型后显示推理策略。", 11, TextAnchor.UpperLeft, new Vector2(122f, -210f), new Vector2(280f, 38f), FontStyle.Normal, Muted);
		text2.horizontalOverflow = HorizontalWrapMode.Wrap;
		text2.verticalOverflow = VerticalWrapMode.Truncate;
		ProfileWidgets rowWidgets = new ProfileWidgets(profileId, gameObject, rootImage, outline, title, text, inputField, inputField2, inputField3, protocolButton, modelButton, modelUp, modelDown, reasonMode, reasonEffort, effortUp, effortDown, reasonKnown, text2, moveUp, moveDown, save, refresh, cancel);
		_rows.Add(profileId, rowWidgets);
		inputField.onValueChanged.AddListener((Action<string>)delegate
		{
			RunUiCallback("UrlChanged", delegate
			{
				NotifyDraft(rowWidgets);
			});
		});
		inputField2.onValueChanged.AddListener((Action<string>)delegate
		{
			RunUiCallback("ApiKeyChanged", delegate
			{
				NotifyDraft(rowWidgets);
			});
		});
		inputField3.onValueChanged.AddListener((Action<string>)delegate
		{
			RunUiCallback("ModelSearchChanged", delegate
			{
				UpdateModelCaption(rowWidgets);
			});
		});
		RefreshProfileVisibility();
		return rowWidgets;
	}

	[HideFromIl2Cpp]
	private void ApplyProfile(ProfileWidgets row, LlmProfileViewState state, bool clearKey)
	{
		row.IsTop = state.IsTop;
		row.Dirty = state.Dirty;
		row.RootImage.color = (state.Dirty ? DirtyRowBackground : (state.IsTop ? ActiveRowBackground : RowBackground));
		row.Border.effectColor = (state.Dirty ? Warning : new Color(0.08f, 0.08f, 0.08f, 1f));
		row.Title.text = (state.IsTop ? "● 当前  " : "○ 候选  ") + state.DisplayName + (state.Dirty ? "  [未保存]" : "");
		if (_profileSelectors.TryGetValue(row.ProfileId, out ProfileSelectorWidgets value))
		{
			value.DisplayName = state.DisplayName;
			value.IsTop = state.IsTop;
			value.Dirty = state.Dirty;
		}
		row.Status.text = state.Status;
		row.Status.color = StatusColor(state.Status, state.Dirty);
		if (!string.Equals(row.Url.text, state.BaseUrl, StringComparison.Ordinal))
		{
			row.Url.SetTextWithoutNotify(state.BaseUrl);
		}
		if (clearKey && row.ApiKey.text.Length != 0)
		{
			row.ApiKey.SetTextWithoutNotify("");
		}
		SetPlaceholder(row.ApiKey, state.HasSavedApiKey ? "已安全保存（留空表示保留）" : "输入 API Key");
		row.Protocol = state.Protocol;
		row.Model = state.SelectedModel;
		row.Models = state.Models.ToArray();
		row.ReasoningMode = state.ReasoningMode;
		row.ReasoningEffort = state.ReasoningEffort;
		row.AllowedEfforts = state.AllowedReasoningEfforts.ToArray();
		row.ReasonKnown.text = (state.KnownReasoningModel ? "已识别推理" : "能力未知");
		row.ReasonKnown.color = (state.KnownReasoningModel ? Good : Muted);
		row.ReasonDescription.text = state.EffectiveReasoning;
		row.SaveButtonText.text = (state.Dirty ? "保存/重检 *" : "保存并检测");
		UpdateProtocolCaption(row);
		UpdateModelCaption(row);
		UpdateReasoningCaptions(row);
		UpdateProfileSelectorCaption(row.ProfileId);
	}

	[HideFromIl2Cpp]
	private void SetRowPosition(ProfileWidgets row, int index)
	{
		row.Root.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -91f);
		row.Order = index;
		if (_profileSelectors.TryGetValue(row.ProfileId, out ProfileSelectorWidgets value))
		{
			value.Order = index;
			value.Button.GetComponent<RectTransform>().anchoredPosition = new Vector2(8 + index * 134, -32f);
			UpdateProfileSelectorCaption(row.ProfileId);
		}
		RefreshProfileVisibility();
	}

	private void SelectVisibleProfile(string profileId)
	{
		if (_rows.ContainsKey(profileId))
		{
			_visibleProfileId = profileId;
			RefreshProfileVisibility();
		}
	}

	private void RefreshProfileVisibility()
	{
		if (_visibleProfileId == null || !_rows.ContainsKey(_visibleProfileId))
		{
			_visibleProfileId = _rows.Values.OrderBy((ProfileWidgets row) => row.Order).FirstOrDefault()?.ProfileId;
		}
		foreach (ProfileWidgets value in _rows.Values)
		{
			value.Root.SetActive(string.Equals(value.ProfileId, _visibleProfileId, StringComparison.Ordinal));
		}
		foreach (string key in _profileSelectors.Keys)
		{
			UpdateProfileSelectorCaption(key);
		}
	}

	private void UpdateProfileSelectorCaption(string profileId)
	{
		if (_profileSelectors.TryGetValue(profileId, out ProfileSelectorWidgets value))
		{
			bool flag = string.Equals(profileId, _visibleProfileId, StringComparison.Ordinal);
			string text = (string.IsNullOrWhiteSpace(value.DisplayName) ? $"配置 {value.Order + 1}" : value.DisplayName);
			value.Label.text = (flag ? "▶ " : "") + text + (value.IsTop ? " · 当前" : "") + (value.Dirty ? " *" : "");
		}
	}

	[HideFromIl2Cpp]
	private void NotifyDraft(ProfileWidgets row)
	{
		if (!_suppress && !IsStopped)
		{
			_callbacks.DraftChanged?.Invoke(Gather(row));
		}
	}

	[HideFromIl2Cpp]
	private LlmProfileDraft Gather(ProfileWidgets row)
	{
		return new LlmProfileDraft(row.ProfileId, row.Url.text, row.ApiKey.text, row.Protocol, row.Model, row.ReasoningMode, row.ReasoningEffort);
	}

	private void CycleModel(string profileId, int delta)
	{
		if (!_rows.TryGetValue(profileId, out ProfileWidgets row))
		{
			return;
		}
		List<string> list = FilteredModels(row);
		if (list.Count != 0)
		{
			int num = list.FindIndex((string model) => model == row.Model);
			num = ((num >= 0) ? ((num + delta + list.Count) % list.Count) : ((delta < 0) ? (list.Count - 1) : 0));
			row.Model = list[num];
			UpdateModelCaption(row);
			NotifyDraft(row);
		}
	}

	private static List<string> FilteredModels(ProfileWidgets row)
	{
		string query = row.Search.text.Trim();
		if (query.Length != 0)
		{
			return row.Models.Where((string model) => model.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
		}
		return row.Models.ToList();
	}

	private static void UpdateModelCaption(ProfileWidgets row)
	{
		string text = (row.Dirty ? " *" : "");
		if (row.Models.Length == 0)
		{
			row.ModelButtonText.text = (string.IsNullOrEmpty(row.Model) ? "尚未检测模型" : (Compact(row.Model, 18) + "·未刷新" + text));
			return;
		}
		List<string> list = FilteredModels(row);
		if (list.Count == 0)
		{
			row.ModelButtonText.text = (string.IsNullOrEmpty(row.Model) ? "没有匹配模型" : (Compact(row.Model, 16) + "·无匹配" + text));
			return;
		}
		int num = list.FindIndex((string model) => model == row.Model);
		row.ModelButtonText.text = (string.IsNullOrEmpty(row.Model) ? $"未选择 · {list.Count} 个{text}" : ((num < 0) ? (Compact(row.Model, 15) + "·不在筛选" + text) : $"{Compact(row.Model, 18)} [{num + 1}/{list.Count}]{text}"));
	}

	private void CycleReasonMode(string profileId)
	{
		if (_rows.TryGetValue(profileId, out ProfileWidgets value))
		{
			ProfileWidgets profileWidgets = value;
			string reasoningMode = value.ReasoningMode;
			string reasoningMode2 = ((reasoningMode == "auto") ? "omit" : ((!(reasoningMode == "omit")) ? "auto" : "custom"));
			profileWidgets.ReasoningMode = reasoningMode2;
			UpdateReasoningCaptions(value);
			NotifyDraft(value);
		}
	}

	private void CycleReasonEffort(string profileId, int delta)
	{
		if (_rows.TryGetValue(profileId, out ProfileWidgets value) && !(value.ReasoningMode != "custom") && value.AllowedEfforts.Length != 0)
		{
			int num = Array.IndexOf(value.AllowedEfforts, value.ReasoningEffort);
			num = ((num >= 0) ? ((num + delta + value.AllowedEfforts.Length) % value.AllowedEfforts.Length) : 0);
			value.ReasoningEffort = value.AllowedEfforts[num];
			UpdateReasoningCaptions(value);
			NotifyDraft(value);
		}
	}

	private static void UpdateProtocolCaption(ProfileWidgets row)
	{
		row.ProtocolButtonText.text = ((row.Protocol == "responses") ? "Responses" : "Chat Completions");
	}

	private static void UpdateReasoningCaptions(ProfileWidgets row)
	{
		Text reasonModeText = row.ReasonModeText;
		string reasoningMode = row.ReasoningMode;
		string text = ((reasoningMode == "omit") ? "不发送推理参数" : ((!(reasoningMode == "custom")) ? "自动最低" : "自定义推理"));
		reasonModeText.text = text;
		row.ReasonEffortText.text = ((row.ReasoningMode == "custom") ? row.ReasoningEffort : "—");
	}

	private void RefreshInteractivity()
	{
		bool flag = !_connectionBusy && !_chatBusy;
		foreach (ProfileSelectorWidgets value in _profileSelectors.Values)
		{
			value.Button.interactable = flag;
		}
		foreach (ProfileWidgets value2 in _rows.Values)
		{
			value2.Url.interactable = flag;
			value2.ApiKey.interactable = flag;
			value2.Search.interactable = flag && value2.Models.Length != 0;
			value2.ProtocolButton.interactable = flag;
			value2.ModelButton.interactable = flag && value2.Models.Length != 0;
			value2.ModelUp.interactable = flag && value2.Models.Length != 0;
			value2.ModelDown.interactable = flag && value2.Models.Length != 0;
			value2.ReasonMode.interactable = flag;
			bool interactable = flag && value2.ReasoningMode == "custom" && value2.AllowedEfforts.Length != 0;
			value2.ReasonEffort.interactable = interactable;
			value2.EffortUp.interactable = interactable;
			value2.EffortDown.interactable = interactable;
			value2.MoveUp.interactable = flag && value2.Order > 0;
			value2.MoveDown.interactable = flag && value2.Order < 2;
			value2.Save.interactable = flag;
			value2.Refresh.interactable = flag;
			value2.Cancel.interactable = _connectionBusy && _busyProfileId == value2.ProfileId;
		}
		if (_chatInput != null)
		{
			_chatInput.interactable = !_chatBusy && !_connectionBusy;
		}
		if (_chatSend != null)
		{
			_chatSend.interactable = !_chatBusy && !_connectionBusy && _chatInput != null && !string.IsNullOrWhiteSpace(_chatInput.text);
			Text componentInChildren = _chatSend.GetComponentInChildren<Text>();
			if (componentInChildren != null)
			{
				componentInChildren.text = (_chatBusy ? "发送中…" : "发送");
			}
		}
	}

	private void HandleKeyboard()
	{
		bool num = Input.compositionString.Length != 0;
		if (num)
		{
			_lastCompositionFrame = Time.frameCount;
		}
		bool flag = !num && Time.frameCount > _lastCompositionFrame + 1;
		ChatHotkeySample chatHotkeySample = _chatHotkey.Poll();
		if ((_f7Deduplicator.Consume(chatHotkeySample.EdgeObserved, chatHotkeySample.IsDown, Time.unscaledTime) & flag) && !_chatHotkey.IsCapturing && !IsGameHotkeyCaptureActive())
		{
			ToggleChat(chatHotkeySample.Source);
		}
		bool flag2 = _chatInput != null && _chatInput.isFocused && _chatPanel != null && _chatPanel.activeSelf;
		if (flag2)
		{
			_chatFocusPendingUntil = 0f;
		}
		if (((flag2 && !_chatBusy) & flag) && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
		{
			RequestChatSubmit("unity_key_edge");
		}
		if (flag2 && Input.GetKeyDown(KeyCode.Escape))
		{
			CloseChat("escape");
		}
		if (IsChatFocusPending() && !flag2)
		{
			if (HasExternalUiSelection())
			{
				_chatFocusPendingUntil = 0f;
			}
			else
			{
				FocusChatAfterKeyboardLease();
			}
		}
		bool needed = IsChatFocusPending() || _allInputs.Any((InputField input) => input != null && input.isFocused);
		_keyboard.SetNeeded(needed);
	}

	private bool AnySettingsFieldFocused()
	{
		return _allInputs.Any((InputField input) => input != null && input != _chatInput && input.isFocused);
	}

	private void ToggleChat(string source)
	{
		if (!(_chatPanel == null))
		{
			if (_chatPanel.activeSelf)
			{
				CloseChat(source);
			}
			else if (!AnySettingsFieldFocused() && !HasExternalUiSelection())
			{
				OpenChat(source);
			}
		}
	}

	private bool HasExternalUiSelection()
	{
		GameObject gameObject = EventSystem.current?.currentSelectedGameObject;
		if (gameObject == null)
		{
			return false;
		}
		bool num = _uiRoot != null && gameObject.transform.IsChildOf(_uiRoot.transform);
		bool flag = _settingsPanel != null && (gameObject == _settingsPanel || gameObject.transform.IsChildOf(_settingsPanel.transform));
		bool flag2 = _gameSettingsTab != null && (gameObject == _gameSettingsTab || gameObject.transform.IsChildOf(_gameSettingsTab.transform));
		if (num | flag | flag2)
		{
			return false;
		}
		return HasFocusedInputComponent(gameObject.transform);
	}

	private static bool HasFocusedInputComponent(Transform selected)
	{
		Transform transform = selected;
		while (transform != null)
		{
			InputField component = transform.GetComponent<InputField>();
			if ((object)component != null)
			{
				return component.isFocused;
			}
			try
			{
				foreach (Component component2 in transform.GetComponents<Component>())
				{
					if (!(component2 == null))
					{
						Type type = component2.GetType();
						if ((type.FullName ?? type.Name).Contains("InputField", StringComparison.OrdinalIgnoreCase))
						{
							return !(type.GetProperty("isFocused", BindingFlags.Instance | BindingFlags.Public)?.GetValue(component2) is bool flag) || flag;
						}
					}
				}
			}
			catch
			{
				string text = transform.name ?? "";
				if (text.Contains("InputField", StringComparison.OrdinalIgnoreCase) || text.Contains("EditBox", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
			transform = transform.parent;
		}
		return false;
	}

	private static bool IsGameHotkeyCaptureActive()
	{
		try
		{
			object obj = Type.GetType("HotkeyService, Assembly-CSharp", throwOnError: false)?.GetProperty("CapturingAction", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
			if (obj == null)
			{
				return false;
			}
			object obj2 = obj.GetType().GetProperty("HasValue", BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj);
			return obj2 is bool && (bool)obj2;
		}
		catch
		{
			return false;
		}
	}

	private void OpenChat(string source)
	{
		if (!(_chatPanel == null) && !(_chatInput == null))
		{
			bool flag = _keyboard.SetNeeded(needed: true);
			_chatPanel.SetActive(value: true);
			_chatPanel.transform.SetAsLastSibling();
			_chatFocusPendingUntil = Time.unscaledTime + 0.45f;
			if (flag)
			{
				FocusChatAfterKeyboardLease(leaseAlreadyHeld: true);
			}
			SafeLogInfo("module=UI; state=chat_opened; source=" + source + "; keyboard_lease=" + flag.ToString().ToLowerInvariant());
		}
	}

	private bool IsChatFocusPending()
	{
		if (_chatPanel != null && _chatPanel.activeSelf)
		{
			return Time.unscaledTime <= _chatFocusPendingUntil;
		}
		return false;
	}

	private void FocusChatAfterKeyboardLease(bool leaseAlreadyHeld = false)
	{
		if (!(_chatPanel == null) && _chatPanel.activeSelf && !(_chatInput == null) && (leaseAlreadyHeld || _keyboard.SetNeeded(needed: true)))
		{
			EventSystem current = EventSystem.current;
			if (current != null && current.currentSelectedGameObject != _chatInput.gameObject)
			{
				current.SetSelectedGameObject(_chatInput.gameObject);
			}
			_chatInput.Select();
			_chatInput.ActivateInputField();
			if (_chatInput.isFocused)
			{
				_chatFocusPendingUntil = 0f;
			}
		}
	}

	private void CloseChat(string source)
	{
		if (!(_chatPanel == null))
		{
			_chatFocusPendingUntil = 0f;
			_chatInput?.DeactivateInputField();
			ClearOwnSelection();
			_chatPanel.SetActive(value: false);
			_keyboard.SetNeeded(AnySettingsFieldFocused());
			SafeLogInfo("module=UI; state=chat_closed; source=" + source);
		}
	}

	private void RequestChatSubmit(string source)
	{
		if (!(_chatInput == null) && !_chatBusy && !_connectionBusy && !string.IsNullOrWhiteSpace(_chatInput.text) && Input.compositionString.Length == 0 && Time.frameCount > _lastCompositionFrame + 1 && _lastChatSubmitFrame != Time.frameCount)
		{
			_lastChatSubmitFrame = Time.frameCount;
			_chatFocusPendingUntil = 0f;
			SafeLogInfo($"module=UI; state=chat_submit_requested; source={source}; characters={_chatInput.text.Length}");
			_callbacks.SendRequested?.Invoke(_chatInput.text);
		}
	}

	private void SelectNativeApiTab()
	{
		try
		{
			if (!_applicationQuitting && !IsStopped && !(_settingsPanel == null) && _traySettingsView != null && _trayItemRoot != null)
			{
				Transform transform = _trayItemRoot.GetValue(_traySettingsView) as Transform;
				if (!(transform == null))
				{
					SetAllNativeTabsSelected(selected: false);
					_apiTabSelected = true;
					ApplyApiTabVisual(selected: true);
					MountSettingsBesideNativeView(transform);
					SafeLogInfo($"module=UI; state=interactive_verified; source=native_api_tab_pointer_click; tab_selected=true; panel_active={_settingsPanel.activeInHierarchy.ToString().ToLowerInvariant()}; content_parent={HierarchyPath(_settingsPanel.transform.parent)}");
				}
			}
		}
		catch (Exception error)
		{
			SafeLogCallbackError("SelectNativeApiTab", error);
			try
			{
				RestoreNativeSettingsView(restoreCurrentTabVisual: true);
			}
			catch
			{
			}
		}
	}

	private void ClearOwnSelection()
	{
		GameObject gameObject = EventSystem.current?.currentSelectedGameObject;
		bool flag = gameObject != null && _uiRoot != null && gameObject.transform.IsChildOf(_uiRoot.transform);
		bool flag2 = gameObject != null && _settingsPanel != null && (gameObject == _settingsPanel || gameObject.transform.IsChildOf(_settingsPanel.transform));
		bool flag3 = gameObject != null && _chatPanel != null && (gameObject == _chatPanel || gameObject.transform.IsChildOf(_chatPanel.transform));
		bool flag4 = gameObject != null && _gameSettingsTab != null && (gameObject == _gameSettingsTab || gameObject.transform.IsChildOf(_gameSettingsTab.transform));
		if (gameObject != null && (flag | flag2 | flag3 | flag4))
		{
			EventSystem.current.SetSelectedGameObject(null);
		}
	}

	private void MaintainNativeApiTab()
	{
		if (Time.unscaledTime < _nextTrayScan)
		{
			return;
		}
		_nextTrayScan = Time.unscaledTime + 0.2f;
		try
		{
			if (!ResolveTraySettings())
			{
				if (_gameSettingsTab != null)
				{
					_gameSettingsTab.SetActive(value: false);
				}
				ReportTrayDiagnostic($"state=unresolved; candidates={_trayCandidateCount}");
				return;
			}
			object traySettingsView = _traySettingsView;
			if (!(traySettingsView is UnityEngine.Object obj) || obj == null)
			{
				InvalidateTraySettings();
				return;
			}
			object obj2 = _trayVisible?.GetValue(traySettingsView);
			bool flag = obj2 is bool && (bool)obj2;
			int value = Convert.ToInt32(_trayTab?.GetValue(traySettingsView));
			Transform transform = _trayItemRoot?.GetValue(traySettingsView) as Transform;
			Transform transform2 = _trayTabContainer?.GetValue(traySettingsView) as Transform;
			if (transform == null || transform2 == null)
			{
				InvalidateTraySettings();
				return;
			}
			EnsureNativeApiTab(transform2);
			ApplyNativeChatSkin(transform);
			if (_gameSettingsTab != null)
			{
				_gameSettingsTab.SetActive(value: true);
			}
			if (!flag)
			{
				if (_apiTabSelected)
				{
					RestoreNativeSettingsView(restoreCurrentTabVisual: true);
				}
				ReportTrayDiagnostic($"state=mounted_hidden; candidates={_trayCandidateCount}; tab={value}; tab_parent={HierarchyPath(transform2)}");
				return;
			}
			if (_apiTabSelected && (transform.gameObject.activeSelf || AnyNativeTabSelected()))
			{
				RestoreNativeSettingsView();
			}
			else if (_apiTabSelected)
			{
				MountSettingsBesideNativeView(transform);
			}
			RectTransform rectTransform = _gameSettingsTab?.GetComponent<RectTransform>();
			ReportTrayDiagnostic($"state=mounted; candidates={_trayCandidateCount}; native_tab=true; selected={_apiTabSelected.ToString().ToLowerInvariant()}; current_tab={value}; tab_parent={HierarchyPath(transform2)}; sibling={_gameSettingsTab?.transform.GetSiblingIndex() ?? (-1)}; tab_width={rectTransform?.rect.width ?? 0f:0.##}; tab_height={rectTransform?.rect.height ?? 0f:0.##}; content_native_sibling=true");
		}
		catch (Exception ex)
		{
			InvalidateTraySettings();
			ReportTrayDiagnostic("state=error; type=" + ex.GetType().Name + "; message=" + SingleLine(ex.Message));
		}
	}

	private bool ResolveTraySettings()
	{
		IReadOnlyList<LocatedUnityObject> readOnlyList = UnityRuntimeObjectLocator.FindAll("UI.TraySettingNew.TraySettingNewView, Assembly-CSharp");
		_trayCandidateCount = readOnlyList.Count;
		(LocatedUnityObject, RuntimeValueAccessor, RuntimeValueAccessor, RuntimeValueAccessor, RuntimeValueAccessor, RuntimeValueAccessor, RuntimeValueAccessor)? tuple = null;
		(LocatedUnityObject, RuntimeValueAccessor, RuntimeValueAccessor, RuntimeValueAccessor, RuntimeValueAccessor, RuntimeValueAccessor, RuntimeValueAccessor)? tuple2 = null;
		foreach (LocatedUnityObject item in readOnlyList)
		{
			RuntimeValueAccessor runtimeValueAccessor = RuntimeValueAccessor.Find(item.ReflectedType, "IsVisible");
			RuntimeValueAccessor runtimeValueAccessor2 = RuntimeValueAccessor.Find(item.ReflectedType, "_currentTab");
			RuntimeValueAccessor runtimeValueAccessor3 = RuntimeValueAccessor.Find(item.ReflectedType, "_settingItemRoot");
			RuntimeValueAccessor runtimeValueAccessor4 = RuntimeValueAccessor.Find(item.ReflectedType, "_tabItemPrefab");
			RuntimeValueAccessor runtimeValueAccessor5 = RuntimeValueAccessor.Find(item.ReflectedType, "_tabItemContainer");
			RuntimeValueAccessor runtimeValueAccessor6 = RuntimeValueAccessor.Find(item.ReflectedType, "_tabItems");
			if (runtimeValueAccessor == null || runtimeValueAccessor2 == null || runtimeValueAccessor3 == null || runtimeValueAccessor4 == null || runtimeValueAccessor5 == null || runtimeValueAccessor6 == null)
			{
				continue;
			}
			(LocatedUnityObject, RuntimeValueAccessor, RuntimeValueAccessor, RuntimeValueAccessor, RuntimeValueAccessor, RuntimeValueAccessor, RuntimeValueAccessor) tuple3 = (item, runtimeValueAccessor, runtimeValueAccessor2, runtimeValueAccessor3, runtimeValueAccessor4, runtimeValueAccessor5, runtimeValueAccessor6);
			(LocatedUnityObject, RuntimeValueAccessor, RuntimeValueAccessor, RuntimeValueAccessor, RuntimeValueAccessor, RuntimeValueAccessor, RuntimeValueAccessor) valueOrDefault = tuple.GetValueOrDefault();
			if (!tuple.HasValue)
			{
				valueOrDefault = tuple3;
				tuple = valueOrDefault;
			}
			try
			{
				object value = runtimeValueAccessor.GetValue(item.Instance);
				if (value is bool && (bool)value)
				{
					valueOrDefault = tuple2.GetValueOrDefault();
					if (!tuple2.HasValue)
					{
						valueOrDefault = tuple3;
						tuple2 = valueOrDefault;
					}
					if (runtimeValueAccessor3.GetValue(item.Instance) is Transform transform && transform.gameObject.activeInHierarchy)
					{
						break;
					}
				}
			}
			catch (Exception)
			{
			}
		}
		(LocatedUnityObject, RuntimeValueAccessor, RuntimeValueAccessor, RuntimeValueAccessor, RuntimeValueAccessor, RuntimeValueAccessor, RuntimeValueAccessor)? tuple4 = tuple2 ?? tuple;
		if (tuple4.HasValue)
		{
			(LocatedUnityObject, RuntimeValueAccessor, RuntimeValueAccessor, RuntimeValueAccessor, RuntimeValueAccessor, RuntimeValueAccessor, RuntimeValueAccessor) valueOrDefault2 = tuple4.GetValueOrDefault();
			BindTraySettings(valueOrDefault2.Item1, valueOrDefault2.Item2, valueOrDefault2.Item3, valueOrDefault2.Item4, valueOrDefault2.Item5, valueOrDefault2.Item6, valueOrDefault2.Item7);
			return true;
		}
		InvalidateTraySettings();
		return false;
	}

	[HideFromIl2Cpp]
	private void BindTraySettings(LocatedUnityObject located, RuntimeValueAccessor visible, RuntimeValueAccessor tab, RuntimeValueAccessor root, RuntimeValueAccessor tabPrefab, RuntimeValueAccessor tabContainer, RuntimeValueAccessor tabItems)
	{
		if (!(_traySettingsView is UnityEngine.Object obj) || !(obj != null) || !(located.Instance is UnityEngine.Object obj2) || !(obj2 != null) || obj.GetInstanceID() != obj2.GetInstanceID())
		{
			RestoreNativeSettingsView();
			if (_gameSettingsTab != null)
			{
				try
				{
					_gameSettingsTabButton?.onClick.RemoveAllListeners();
				}
				catch
				{
				}
				UnityEngine.Object.Destroy(_gameSettingsTab);
			}
			_gameSettingsTab = null;
			_gameSettingsTabButton = null;
			_gameSettingsTabTitle = null;
		}
		_traySettingsView = located.Instance;
		_trayVisible = visible;
		_trayTab = tab;
		_trayItemRoot = root;
		_trayTabPrefab = tabPrefab;
		_trayTabContainer = tabContainer;
		_trayTabItems = tabItems;
	}

	[HideFromIl2Cpp]
	private void EnsureNativeApiTab(Transform tabContainer)
	{
		if (_gameSettingsTab != null && _gameSettingsTab.transform.parent == tabContainer)
		{
			WriteRuntimeText(_gameSettingsTabTitle, "API 设置");
			PlaceApiTabAfterOther(tabContainer);
			ApplyApiTabVisual(_apiTabSelected);
		}
		else
		{
			if (_traySettingsView == null || _trayTabItems == null || _trayTabPrefab == null)
			{
				return;
			}
			object value = _trayTabPrefab.GetValue(_traySettingsView);
			GameObject gameObject = (value as Component)?.gameObject;
			if (!(gameObject == null) && value != null)
			{
				GameObject gameObject2 = UnityEngine.Object.Instantiate(gameObject, tabContainer, worldPositionStays: false);
				gameObject2.name = "Just_Lilith.NativeTab.ApiSettings";
				SetLayerRecursively(gameObject2.transform, tabContainer.gameObject.layer);
				Type type = value.GetType();
				object componentByManagedType = GetComponentByManagedType(gameObject2, type);
				if (componentByManagedType is Behaviour behaviour)
				{
					behaviour.enabled = false;
				}
				Button button = (RuntimeValueAccessor.Find(type, "_button")?.GetValue(componentByManagedType) as Button) ?? gameObject2.GetComponentInChildren<Button>(includeInactive: true);
				if (button == null)
				{
					throw new InvalidOperationException("Native tab prefab has no Button.");
				}
				button.onClick.RemoveAllListeners();
				button.onClick.AddListener((Action)SelectNativeApiTab);
				_gameSettingsTab = gameObject2;
				_gameSettingsTabButton = button;
				_gameSettingsTabImage = (RuntimeValueAccessor.Find(type, "_image")?.GetValue(componentByManagedType) as Image) ?? gameObject2.GetComponent<Image>();
				_gameSettingsTabSelectedSprite = RuntimeValueAccessor.Find(type, "_selectedSprite")?.GetValue(componentByManagedType) as Sprite;
				_gameSettingsTabUnselectedSprite = RuntimeValueAccessor.Find(type, "_unselectedSprite")?.GetValue(componentByManagedType) as Sprite;
				_gameSettingsTabTitle = RuntimeValueAccessor.Find(type, "_titleText")?.GetValue(componentByManagedType);
				WriteRuntimeText(_gameSettingsTabTitle, "API 设置");
				gameObject2.SetActive(value: true);
				if (componentByManagedType is UnityEngine.Object obj)
				{
					UnityEngine.Object.Destroy(obj);
				}
				PlaceApiTabAfterOther(tabContainer);
				ApplyApiTabVisual(selected: false);
				RectTransform component = tabContainer.GetComponent<RectTransform>();
				if ((object)component != null)
				{
					LayoutRebuilder.ForceRebuildLayoutImmediate(component);
				}
			}
		}
	}

	[HideFromIl2Cpp]
	private List<object> SnapshotNativeTabItems()
	{
		List<object> list = new List<object>();
		if (_traySettingsView == null)
		{
			return list;
		}
		object obj = _trayTabItems?.GetValue(_traySettingsView);
		if (obj == null)
		{
			return list;
		}
		try
		{
			Type type = obj.GetType();
			PropertyInfo property = type.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
			MethodInfo method = type.GetMethod("get_Count", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
			PropertyInfo property2 = type.GetProperty("Item", BindingFlags.Instance | BindingFlags.Public);
			MethodInfo methodInfo = type.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo methodInfo2) => methodInfo2.Name == "get_Item" && methodInfo2.GetParameters().Length == 1);
			if ((property != null || method != null) && (property2 != null || methodInfo != null))
			{
				int num = Convert.ToInt32(property?.GetValue(obj) ?? method.Invoke(obj, null));
				if (num < 0 || num > 256)
				{
					return list;
				}
				for (int num2 = 0; num2 < num; num2++)
				{
					object[] array = new object[1] { num2 };
					object obj2 = property2?.GetValue(obj, array) ?? methodInfo.Invoke(obj, array);
					if (obj2 != null)
					{
						list.Add(obj2);
					}
				}
				return list;
			}
			if (obj is IEnumerable enumerable)
			{
				foreach (object item in enumerable)
				{
					if (item != null)
					{
						list.Add(item);
					}
				}
			}
		}
		catch
		{
		}
		return list;
	}

	[HideFromIl2Cpp]
	private static int ReadNativeTabValue(object item)
	{
		try
		{
			RuntimeValueAccessor runtimeValueAccessor = RuntimeValueAccessor.Find(item.GetType(), "Tab") ?? RuntimeValueAccessor.Find(item.GetType(), "_tab");
			return (runtimeValueAccessor == null) ? int.MinValue : Convert.ToInt32(runtimeValueAccessor.GetValue(item));
		}
		catch
		{
			return int.MinValue;
		}
	}

	[HideFromIl2Cpp]
	private bool AnyNativeTabSelected()
	{
		foreach (object item in SnapshotNativeTabItems())
		{
			try
			{
				object obj = (RuntimeValueAccessor.Find(item.GetType(), "IsSelected") ?? RuntimeValueAccessor.Find(item.GetType(), "_isSelected"))?.GetValue(item);
				if (obj is bool && (bool)obj)
				{
					return true;
				}
			}
			catch
			{
			}
		}
		return false;
	}

	[HideFromIl2Cpp]
	private void SetAllNativeTabsSelected(bool selected)
	{
		foreach (object item in SnapshotNativeTabItems())
		{
			InvokeSetSelected(item, selected);
		}
	}

	[HideFromIl2Cpp]
	private static void InvokeSetSelected(object? item, bool selected)
	{
		if (item == null)
		{
			return;
		}
		try
		{
			item.GetType().GetMethod("SetSelected", BindingFlags.Instance | BindingFlags.Public, null, new Type[1] { typeof(bool) }, null)?.Invoke(item, new object[1] { selected });
		}
		catch
		{
		}
	}

	[HideFromIl2Cpp]
	private static void WriteRuntimeText(object? text, string title)
	{
		if (text == null)
		{
			return;
		}
		try
		{
			text.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public)?.SetValue(text, title);
		}
		catch
		{
		}
	}

	private void PlaceApiTabAfterOther(Transform tabContainer)
	{
		if (_gameSettingsTab == null)
		{
			return;
		}
		int siblingIndex = tabContainer.childCount - 1;
		foreach (object item in SnapshotNativeTabItems())
		{
			if (ReadNativeTabValue(item) == 6 && item is Component component)
			{
				siblingIndex = Math.Min(component.transform.GetSiblingIndex() + 1, tabContainer.childCount - 1);
				break;
			}
		}
		_gameSettingsTab.transform.SetSiblingIndex(siblingIndex);
	}

	private void ApplyApiTabVisual(bool selected)
	{
		if (_gameSettingsTabImage != null)
		{
			Sprite sprite = (selected ? _gameSettingsTabSelectedSprite : _gameSettingsTabUnselectedSprite);
			if (sprite != null)
			{
				_gameSettingsTabImage.sprite = sprite;
				_gameSettingsTabImage.SetNativeSize();
			}
		}
		WriteRuntimeText(_gameSettingsTabTitle, "API 设置");
		RectTransform rectTransform = _gameSettingsTab?.transform.parent?.GetComponent<RectTransform>();
		if ((object)rectTransform != null)
		{
			LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
		}
	}

	private void MountSettingsBesideNativeView(Transform itemRoot)
	{
		if (!(_settingsPanel == null) && !(itemRoot.parent == null))
		{
			ApplyNativePageSkin(itemRoot);
			RectTransform component = itemRoot.GetComponent<RectTransform>();
			RectTransform component2 = _settingsPanel.GetComponent<RectTransform>();
			_settingsPanel.transform.SetParent(itemRoot.parent, worldPositionStays: false);
			if (component != null && component2 != null)
			{
				CopyRectTransform(component, component2);
			}
			SetLayerRecursively(_settingsPanel.transform, itemRoot.gameObject.layer);
			itemRoot.gameObject.SetActive(value: false);
			_settingsPanel.SetActive(value: true);
			_settingsPanel.transform.SetSiblingIndex(itemRoot.GetSiblingIndex() + 1);
		}
	}

	private void ApplyNativePageSkin(Transform itemRoot)
	{
		if (!(_settingsPanel == null) && _nativeSkinRootId != itemRoot.GetInstanceID() && NativeTrayPageSkin.TryApply(_settingsPanel, itemRoot, out string diagnostic))
		{
			_nativeSkinRootId = itemRoot.GetInstanceID();
			SafeLogInfo("module=UI; state=native_skin_applied; " + diagnostic);
		}
	}

	private void ApplyNativeChatSkin(Transform itemRoot)
	{
		if (!(_chatPanel == null) && _nativeChatSkinRootId != itemRoot.GetInstanceID() && NativeTrayPageSkin.TryApplyChat(_chatPanel, itemRoot, out string diagnostic))
		{
			_nativeChatSkinRootId = itemRoot.GetInstanceID();
			SafeLogInfo("module=UI; state=native_chat_skin_applied; " + diagnostic);
		}
	}

	private void RestoreNativeSettingsView(bool restoreCurrentTabVisual = false)
	{
		try
		{
			ClearOwnSelection();
		}
		catch
		{
		}
		object traySettingsView = _traySettingsView;
		Transform transform = null;
		try
		{
			transform = ((traySettingsView == null) ? null : (_trayItemRoot?.GetValue(traySettingsView) as Transform));
		}
		catch
		{
		}
		if (transform != null)
		{
			transform.gameObject.SetActive(value: true);
		}
		_apiTabSelected = false;
		ApplyApiTabVisual(selected: false);
		if (restoreCurrentTabVisual && traySettingsView != null)
		{
			try
			{
				int num = Convert.ToInt32(_trayTab?.GetValue(traySettingsView));
				foreach (object item in SnapshotNativeTabItems())
				{
					InvokeSetSelected(item, ReadNativeTabValue(item) == num);
				}
			}
			catch
			{
			}
		}
		if (_settingsPanel != null)
		{
			_settingsPanel.SetActive(value: false);
			if (_settingsPanelHome != null)
			{
				_settingsPanel.transform.SetParent(_settingsPanelHome, worldPositionStays: false);
				ResetSettingsOverlayRect(_settingsPanel.GetComponent<RectTransform>());
			}
		}
	}

	private static void CopyRectTransform(RectTransform source, RectTransform target)
	{
		target.anchorMin = source.anchorMin;
		target.anchorMax = source.anchorMax;
		target.pivot = source.pivot;
		target.anchoredPosition = source.anchoredPosition;
		target.sizeDelta = source.sizeDelta;
		target.localScale = Vector3.one;
		target.localRotation = Quaternion.identity;
	}

	private static void ResetSettingsOverlayRect(RectTransform? rect)
	{
		if (!(rect == null))
		{
			rect.anchorMin = new Vector2(0.5f, 0.5f);
			rect.anchorMax = new Vector2(0.5f, 0.5f);
			rect.pivot = new Vector2(0.5f, 0.5f);
			rect.anchoredPosition = Vector2.zero;
			rect.sizeDelta = new Vector2(436f, 390f);
			rect.localScale = Vector3.one;
			rect.localRotation = Quaternion.identity;
		}
	}

	private static void SetLayerRecursively(Transform root, int layer)
	{
		root.gameObject.layer = layer;
		for (int i = 0; i < root.childCount; i++)
		{
			SetLayerRecursively(root.GetChild(i), layer);
		}
	}

	[HideFromIl2Cpp]
	private static object? GetComponentByManagedType(GameObject root, Type componentType)
	{
		try
		{
			return typeof(GameObject).GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo method) => method.Name == "GetComponent" && method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 1 && method.GetParameters().Length == 0)?.MakeGenericMethod(componentType).Invoke(root, null);
		}
		catch
		{
			return null;
		}
	}

	private void InvalidateTraySettings()
	{
		DetachLilithSpeechRows();
		DetachLilithConversationRows();
		DetachAgentRows();
		RestoreNativeSettingsView();
		if (_gameSettingsTab != null)
		{
			try
			{
				_gameSettingsTabButton?.onClick.RemoveAllListeners();
			}
			catch
			{
			}
			UnityEngine.Object.Destroy(_gameSettingsTab);
		}
		_gameSettingsTab = null;
		_gameSettingsTabButton = null;
		_gameSettingsTabTitle = null;
		_gameSettingsTabImage = null;
		_gameSettingsTabSelectedSprite = null;
		_gameSettingsTabUnselectedSprite = null;
		_nativeSkinRootId = 0;
		_nativeChatSkinRootId = 0;
		_traySettingsView = null;
		_trayVisible = null;
		_trayTab = null;
		_trayItemRoot = null;
		_trayTabPrefab = null;
		_trayTabContainer = null;
		_trayTabItems = null;
	}

	private void ReportTrayDiagnostic(string value)
	{
		if (!string.Equals(_lastTrayDiagnostic, value, StringComparison.Ordinal))
		{
			_lastTrayDiagnostic = value;
			SafeLogInfo("module=UI; entry=native_api_settings_tab; " + value);
		}
	}

	[HideFromIl2Cpp]
	private void RunUiCallback(string callback, Action action)
	{
		if (_applicationQuitting || IsStopped)
		{
			return;
		}
		try
		{
			action();
		}
		catch (Exception error)
		{
			SafeLogCallbackError(callback, error);
		}
	}

	[HideFromIl2Cpp]
	private void SafeLogCallbackError(string callback, Exception error)
	{
		try
		{
			string value = error.Message ?? "";
			SafeLogError($"module=UI; state=callback_error_contained; callback={callback}; type={error.GetType().Name}; message={SingleLine(value)}");
		}
		catch
		{
			SafeLogError("module=UI; state=callback_error_contained; callback=" + callback + "; error_format=failed");
		}
	}

	private void SafeLogInfo(string message)
	{
		try
		{
			_log?.LogInfo(message);
		}
		catch
		{
		}
	}

	private void SafeLogError(string message)
	{
		try
		{
			_log?.LogError(message);
		}
		catch
		{
		}
	}

	private static string HierarchyPath(Transform transform)
	{
		Stack<string> stack = new Stack<string>();
		Transform transform2 = transform;
		while (transform2 != null)
		{
			stack.Push(transform2.name);
			transform2 = transform2.parent;
		}
		return string.Join("/", stack);
	}

	private static Color StatusColor(string status, bool dirty)
	{
		if (dirty)
		{
			return Warning;
		}
		if (LooksLikeError(status))
		{
			return Bad;
		}
		if (!status.Contains("已保存", StringComparison.Ordinal) && !status.Contains("检测到", StringComparison.Ordinal))
		{
			return Muted;
		}
		return Good;
	}

	private static bool LooksLikeError(string text)
	{
		if (!text.Contains("失败", StringComparison.Ordinal) && !text.Contains("错误", StringComparison.Ordinal) && !text.Contains("请先", StringComparison.Ordinal) && !text.Contains("未配置", StringComparison.Ordinal))
		{
			return text.Contains("异常", StringComparison.Ordinal);
		}
		return true;
	}

	private static string SingleLine(string value)
	{
		return value.Replace('\r', ' ').Replace('\n', ' ').Trim();
	}

	private static string Compact(string value, int limit)
	{
		if (value.Length <= limit)
		{
			return value;
		}
		return value.Substring(0, Math.Max(1, limit - 1)) + "…";
	}

	private InputField CreateInput(Transform parent, string name, string placeholder, Vector2 topLeft, Vector2 size, bool password, int characterLimit)
	{
		GameObject gameObject = RectObject(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), size, topLeft);
		AddImage(gameObject, FieldBackground);
		gameObject.AddComponent<RectMask2D>();
		InputField inputField = gameObject.AddComponent<InputField>();
		Text text = CreateText(gameObject.transform, "Text", "", 14, TextAnchor.MiddleLeft, new Vector2(8f, -2f), new Vector2(size.x - 16f, size.y - 4f));
		Text text2 = CreateText(gameObject.transform, "Placeholder", placeholder, 14, TextAnchor.MiddleLeft, new Vector2(8f, -2f), new Vector2(size.x - 16f, size.y - 4f), FontStyle.Italic, Muted);
		inputField.textComponent = text;
		inputField.placeholder = text2;
		text.horizontalOverflow = HorizontalWrapMode.Overflow;
		inputField.lineType = InputField.LineType.SingleLine;
		inputField.contentType = (password ? InputField.ContentType.Password : InputField.ContentType.Standard);
		inputField.asteriskChar = '●';
		inputField.characterLimit = characterLimit;
		text.color = InputInk;
		text2.color = InputPlaceholder;
		inputField.caretColor = InputInk;
		inputField.selectionColor = new Color(0.72f, 0.72f, 0.7f, 0.42f);
		_allInputs.Add(inputField);
		return inputField;
	}

	[HideFromIl2Cpp]
	private Button CreateButton(Transform parent, string name, string label, Vector2 topLeft, Vector2 size, Action onClick, bool stretchParent = false)
	{
		GameObject gameObject;
		if (stretchParent)
		{
			gameObject = RectObject(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
			RectTransform component = gameObject.GetComponent<RectTransform>();
			component.offsetMin = new Vector2(2f, 2f);
			component.offsetMax = new Vector2(-2f, -2f);
		}
		else
		{
			gameObject = RectObject(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), size, topLeft);
		}
		Image targetGraphic = AddImage(gameObject, ButtonBackground);
		Button button = gameObject.AddComponent<Button>();
		button.targetGraphic = targetGraphic;
		ColorBlock colors = button.colors;
		colors.normalColor = ButtonBackground;
		colors.highlightedColor = new Color(0.34f, 0.34f, 0.33f, 1f);
		colors.pressedColor = new Color(0.15f, 0.15f, 0.15f, 1f);
		colors.selectedColor = new Color(0.3f, 0.3f, 0.29f, 1f);
		colors.disabledColor = new Color(0.16f, 0.16f, 0.16f, 0.75f);
		button.colors = colors;
		RectTransform rectTransform = CreateText(gameObject.transform, "Label", label, 14, TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero).rectTransform;
		rectTransform.anchorMin = Vector2.zero;
		rectTransform.anchorMax = Vector2.one;
		rectTransform.offsetMin = new Vector2(4f, 2f);
		rectTransform.offsetMax = new Vector2(-4f, -2f);
		button.onClick.AddListener((Action)delegate
		{
			RunUiCallback(name, onClick);
		});
		return button;
	}

	private Text CreateText(Transform parent, string name, string value, int size, TextAnchor alignment, Vector2 topLeft, Vector2 dimensions, FontStyle style = FontStyle.Normal, Color? color = null)
	{
		Text text = RectObject(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), dimensions, topLeft).AddComponent<Text>();
		text.font = _font;
		text.fontSize = size;
		text.fontStyle = style;
		text.alignment = alignment;
		text.color = color ?? Color.white;
		text.supportRichText = false;
		text.horizontalOverflow = HorizontalWrapMode.Overflow;
		text.verticalOverflow = VerticalWrapMode.Truncate;
		text.text = value;
		text.raycastTarget = false;
		return text;
	}

	private static GameObject RectObject(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector2 topLeft)
	{
		GameObject obj = new GameObject(name, Il2CppType.Of<RectTransform>());
		obj.layer = parent.gameObject.layer;
		obj.transform.SetParent(parent, worldPositionStays: false);
		RectTransform component = obj.GetComponent<RectTransform>();
		component.anchorMin = anchorMin;
		component.anchorMax = anchorMax;
		component.pivot = ((anchorMin == anchorMax) ? anchorMin : new Vector2(0.5f, 0.5f));
		component.sizeDelta = size;
		component.anchoredPosition = topLeft;
		return obj;
	}

	private static Image AddImage(GameObject root, Color color)
	{
		Image image = root.AddComponent<Image>();
		image.color = color;
		return image;
	}

	private static void SetPlaceholder(InputField field, string text)
	{
		if (field.placeholder is Text text2)
		{
			text2.text = text;
		}
	}

	private void MaintainNativeLilithConversations()
	{
		if (_applicationQuitting || IsStopped)
		{
			return;
		}
		try
		{
			if (!_apiTabSelected && _traySettingsView != null && !(_uiRoot == null))
			{
				object obj = _trayVisible?.GetValue(_traySettingsView);
				if (obj is bool && (bool)obj && Convert.ToInt32(_trayTab?.GetValue(_traySettingsView)) == 4 && _trayItemRoot?.GetValue(_traySettingsView) is Transform transform && !(transform == null) && transform.gameObject.activeInHierarchy)
				{
					if (_lilithRows != null && !_lilithRows.IsValidFor(transform))
					{
						DetachLilithConversationRows();
					}
					if (_lilithRows == null)
					{
						if (!NativeLilithConversationRows.TryCreate(transform, _uiRoot.transform, delegate
						{
							RunUiCallback("OpenPersonality", delegate
							{
								_callbacks.OpenSystemPromptRequested?.Invoke();
							});
						}, delegate
						{
							RunUiCallback("OpenJourneys", OpenJourneyDialog);
						}, delegate
						{
							RunUiCallback("OpenDreamMemory", OpenDreamMemoryFromLilith);
						}, delegate
						{
							RunUiCallback("OpenWorldBook", OpenWorldBookFromLilith);
						}, 0, out NativeLilithConversationRows result, out string diagnostic))
						{
							ReportJourneyDiagnostic(diagnostic);
							return;
						}
						_lilithRows = result;
						_lilithItemRoot = transform;
						TryReloadJourneys(showError: false);
						_lilithRows.SetJourneyName(_journeyActiveName);
						ReportJourneyDiagnostic(diagnostic);
					}
					if (_lilithRows.MaintainPlacement())
					{
						ReportJourneyDiagnostic("state=placed; native_tab=4; footer=bottom; columns=4");
					}
					return;
				}
			}
			DetachLilithConversationRows();
		}
		catch (Exception error)
		{
			DetachLilithConversationRows();
			SafeLogCallbackError("MaintainNativeLilithConversations", error);
		}
	}

	private void DetachLilithConversationRows()
	{
		CloseJourneyDialog();
		NativeLilithConversationRows? lilithRows = _lilithRows;
		_lilithRows = null;
		_lilithItemRoot = null;
		lilithRows?.Dispose();
	}

	private void ForgetLilithConversationRowsOnQuit()
	{
		_lilithRows = null;
		_lilithItemRoot = null;
		_journeyDialog = null;
		_journeyDragHeader = null;
		_journeyDragging = false;
		_journeyRenameInput = null;
		_journeyDialogStatus = null;
		_journeyPageText = null;
		_journeyDeleteConfirmationId = null;
		_journeyListButtons.Clear();
	}

	private void ReportJourneyDiagnostic(string diagnostic)
	{
		if (!string.Equals(_journeyDiagnostic, diagnostic, StringComparison.Ordinal))
		{
			_journeyDiagnostic = diagnostic;
			SafeLogInfo("module=UI; entry=native_lilith_journeys; " + diagnostic);
		}
	}

	[HideFromIl2Cpp]
	private void OpenDreamMemoryFromLilith()
	{
		try
		{
			_callbacks.OpenDreamMemoryRequested?.Invoke();
		}
		catch (Exception ex)
		{
			SafeLogCallbackError("OpenDreamMemory", ex);
			if (_journeyDialog == null)
			{
				OpenJourneyDialog();
			}
			JourneyStatus("打开幻境记忆失败：" + ex.Message);
		}
	}

	[HideFromIl2Cpp]
	private void OpenWorldBookFromLilith()
	{
		try
		{
			_callbacks.OpenWorldBookRequested?.Invoke();
		}
		catch (Exception ex)
		{
			SafeLogCallbackError("OpenWorldBook", ex);
			if (_journeyDialog == null)
			{
				OpenJourneyDialog();
			}
			JourneyStatus("打开回忆失败：" + ex.Message);
		}
	}

	[HideFromIl2Cpp]
	private void OpenJourneyDialog()
	{
		if (_uiRoot == null || _lilithItemRoot == null)
		{
			return;
		}
		if (_journeyDialog != null)
		{
			CloseJourneyDialog();
			return;
		}
		GameObject gameObject = (_journeyDialog = RectObject("Just_Lilith.Journeys.Dialog", _uiRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(730f, 660f), Vector2.zero));
		AddImage(gameObject, new Color(0.1f, 0.1f, 0.1f, 0.98f)).raycastTarget = true;
		Outline outline = gameObject.AddComponent<Outline>();
		outline.effectColor = Color.white;
		outline.effectDistance = new Vector2(2f, -2f);
		GameObject gameObject2 = RectObject("NativeControls", gameObject.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
		_journeyDragHeader = RectObject("JourneyDragHeader", gameObject.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(590f, 48f), new Vector2(16f, -10f)).GetComponent<RectTransform>();
		_journeyDragging = false;
		CreateText(gameObject2.transform, "Title", "幻境", 22, TextAnchor.MiddleLeft, new Vector2(20f, -16f), new Vector2(600f, 34f), FontStyle.Bold);
		CreateButton(gameObject2.transform, "CloseJourneys", "关闭", new Vector2(622f, -16f), new Vector2(88f, 34f), CloseJourneyDialog);
		for (int i = 0; i < 7; i++)
		{
			int slot = i;
			Button item = CreateButton(gameObject2.transform, "JourneySlot" + i, "", new Vector2(20f, -62 - i * 52), new Vector2(690f, 46f), delegate
			{
				ChooseJourneySlot(slot);
			});
			_journeyListButtons.Add(item);
		}
		CreateButton(gameObject2.transform, "MoveUp", "◀", new Vector2(20f, -436f), new Vector2(75f, 35f), delegate
		{
			MoveJourneyPage(-1);
		});
		_journeyPageText = CreateText(gameObject2.transform, "Page", "1 / 1", 14, TextAnchor.MiddleCenter, new Vector2(100f, -436f), new Vector2(150f, 35f));
		CreateButton(gameObject2.transform, "MoveDown", "▶", new Vector2(255f, -436f), new Vector2(75f, 35f), delegate
		{
			MoveJourneyPage(1);
		});
		CreateButton(gameObject2.transform, "CreateJourney", "＋ 新建幻境", new Vector2(360f, -436f), new Vector2(230f, 35f), CreateJourney);
		CreateButton(gameObject2.transform, "DeleteJourney", "删除幻境", new Vector2(600f, -436f), new Vector2(110f, 35f), DeleteJourney);
		CreateText(gameObject2.transform, "RenameLabel", "当前幻境名称", 15, TextAnchor.MiddleLeft, new Vector2(20f, -495f), new Vector2(180f, 36f));
		_journeyRenameInput = CreateInput(gameObject2.transform, "JourneyName", "输入自定义名称", new Vector2(200f, -495f), new Vector2(390f, 36f), password: false, 64);
		CreateButton(gameObject2.transform, "SaveJourneyName", "保存名称", new Vector2(600f, -495f), new Vector2(110f, 36f), SaveJourneyName);
		_journeyDialogStatus = CreateText(gameObject2.transform, "JourneyStatus", "", 13, TextAnchor.MiddleLeft, new Vector2(20f, -550f), new Vector2(690f, 50f), FontStyle.Normal, Muted);
		_journeyDialogStatus.horizontalOverflow = HorizontalWrapMode.Wrap;
		if (!NativeTrayPageSkin.TryApply(gameObject2, _lilithItemRoot, out string diagnostic))
		{
			ReportJourneyDiagnostic("state=dialog_skin_unresolved; " + diagnostic);
		}
		gameObject.transform.SetAsLastSibling();
		TryReloadJourneys(showError: true);
	}

	private void CloseJourneyDialog()
	{
		bool num = _journeyDialog != null;
		_journeyDragging = false;
		_journeyDragHeader = null;
		if (_journeyDialog != null)
		{
			GameObject gameObject = EventSystem.current?.currentSelectedGameObject;
			if (gameObject != null && gameObject.transform.IsChildOf(_journeyDialog.transform))
			{
				EventSystem.current.SetSelectedGameObject(null);
			}
			if (_journeyRenameInput != null)
			{
				_allInputs.Remove(_journeyRenameInput);
			}
			_journeyDialog.SetActive(value: false);
			UnityEngine.Object.Destroy(_journeyDialog);
		}
		_journeyDialog = null;
		_journeyRenameInput = null;
		_journeyDialogStatus = null;
		_journeyPageText = null;
		_journeyDeleteConfirmationId = null;
		_journeyListButtons.Clear();
		if (num)
		{
			GameKeyboardInputBridge keyboard = _keyboard;
			GameObject? chatPanel = _chatPanel;
			keyboard.SetNeeded(((object)chatPanel != null && chatPanel.activeSelf) || AnySettingsFieldFocused());
		}
	}

	private void MaintainJourneyDialogDrag()
	{
		if (!(_journeyDialog == null) && !(_journeyDragHeader == null) && !(_uiRoot == null))
		{
			RectTransform component = _journeyDialog.GetComponent<RectTransform>();
			if ((object)component != null)
			{
				RectTransform component2 = _uiRoot.GetComponent<RectTransform>();
				if ((object)component2 != null)
				{
					if (!Input.GetMouseButton(0))
					{
						_journeyDragging = false;
						return;
					}
					if (!_journeyDragging)
					{
						if (!Input.GetMouseButtonDown(0) || !RectTransformUtility.RectangleContainsScreenPoint(_journeyDragHeader, Input.mousePosition, null) || !RectTransformUtility.ScreenPointToLocalPointInRectangle(component2, Input.mousePosition, null, out _journeyDragStartPointer))
						{
							return;
						}
						_journeyDragStartPosition = component.anchoredPosition;
						_journeyDragging = true;
						component.SetAsLastSibling();
					}
					if (RectTransformUtility.ScreenPointToLocalPointInRectangle(component2, Input.mousePosition, null, out var localPoint))
					{
						Vector2 vector = _journeyDragStartPosition + localPoint - _journeyDragStartPointer;
						Rect rect = component2.rect;
						float num = component.rect.width / 2f;
						float num2 = component.rect.height / 2f;
						float num3 = rect.xMin + num;
						float num4 = rect.xMax - num;
						float num5 = rect.yMin + num2;
						float num6 = rect.yMax - num2;
						component.anchoredPosition = new Vector2((num3 <= num4) ? Mathf.Clamp(vector.x, num3, num4) : rect.center.x, (num5 <= num6) ? Mathf.Clamp(vector.y, num5, num6) : rect.center.y);
					}
					return;
				}
			}
		}
		_journeyDragging = false;
	}

	private bool TryReloadJourneys(bool showError)
	{
		try
		{
			_journeySessions = _callbacks.ListConversationsRequested?.Invoke() ?? Array.Empty<ActiveConversationInfo>();
			ActiveConversationInfo activeConversationInfo = null;
			int num = -1;
			for (int i = 0; i < _journeySessions.Count; i++)
			{
				if (_journeySessions[i].IsActive)
				{
					activeConversationInfo = _journeySessions[i];
					num = i;
					break;
				}
			}
			if (activeConversationInfo != null)
			{
				_journeyActiveName = activeConversationInfo.DisplayName;
				_journeySelectedId = activeConversationInfo.SessionId;
				_journeyPage = num / 7;
			}
			_lilithRows?.SetJourneyName(_journeyActiveName);
			RenderJourneyList();
			return true;
		}
		catch (Exception ex)
		{
			if (showError)
			{
				JourneyStatus("载入幻境失败：" + ex.Message);
			}
			SafeLogCallbackError("ListJourneys", ex);
			return false;
		}
	}

	private void RenderJourneyList()
	{
		if (_journeyDialog == null)
		{
			return;
		}
		int num = Math.Max(1, (_journeySessions.Count + 7 - 1) / 7);
		_journeyPage = Math.Clamp(_journeyPage, 0, num - 1);
		if (_journeyPageText != null)
		{
			_journeyPageText.text = $"{_journeyPage + 1} / {num}";
		}
		for (int i = 0; i < _journeyListButtons.Count; i++)
		{
			Button button = _journeyListButtons[i];
			int num2 = _journeyPage * 7 + i;
			bool flag = num2 < _journeySessions.Count;
			button.gameObject.SetActive(flag);
			if (flag)
			{
				ActiveConversationInfo activeConversationInfo = _journeySessions[num2];
				button.interactable = activeConversationInfo.ErrorCode == null;
				Text componentInChildren = button.GetComponentInChildren<Text>();
				if (componentInChildren != null)
				{
					componentInChildren.text = (activeConversationInfo.IsActive ? "● " : "○ ") + Compact(activeConversationInfo.DisplayName, 28) + ((activeConversationInfo.ErrorCode == null) ? $"  ·  {activeConversationInfo.TurnCount.GetValueOrDefault()} 轮" : "  ·  文件异常");
				}
			}
		}
		ActiveConversationInfo activeConversationInfo2 = null;
		foreach (ActiveConversationInfo journeySession in _journeySessions)
		{
			if (string.Equals(journeySession.SessionId, _journeySelectedId, StringComparison.Ordinal))
			{
				activeConversationInfo2 = journeySession;
				break;
			}
		}
		if (_journeyRenameInput != null && activeConversationInfo2 != null && !_journeyRenameInput.isFocused)
		{
			_journeyRenameInput.SetTextWithoutNotify(activeConversationInfo2.DisplayName);
		}
	}

	private void ChooseJourneySlot(int slot)
	{
		int num = _journeyPage * 7 + slot;
		if (num >= _journeySessions.Count)
		{
			return;
		}
		ActiveConversationInfo activeConversationInfo = _journeySessions[num];
		if (activeConversationInfo.ErrorCode != null)
		{
			JourneyStatus("该幻境文件需要修复。");
			return;
		}
		_journeyDeleteConfirmationId = null;
		try
		{
			ActiveConversationInfo activeConversationInfo2 = null;
			foreach (ActiveConversationInfo journeySession in _journeySessions)
			{
				if (journeySession.IsActive)
				{
					activeConversationInfo2 = journeySession;
					break;
				}
			}
			_callbacks.SwitchConversationRequested?.Invoke(activeConversationInfo.SessionId, activeConversationInfo2?.SessionId);
			_journeySelectedId = activeConversationInfo.SessionId;
			if (TryReloadJourneys(showError: true))
			{
				JourneyStatus("已切换至：" + _journeyActiveName);
			}
		}
		catch (Exception ex)
		{
			JourneyStatus("切换失败：" + ex.Message);
		}
	}

	private void CreateJourney()
	{
		try
		{
			_journeyDeleteConfirmationId = null;
			string text = _callbacks.StartConversationRequested?.Invoke();
			if (text != null)
			{
				_journeySelectedId = text;
				if (TryReloadJourneys(showError: true))
				{
					JourneyStatus("已新建并切换至：" + _journeyActiveName);
				}
			}
		}
		catch (Exception ex)
		{
			JourneyStatus("新建失败：" + ex.Message);
		}
	}

	private void SaveJourneyName()
	{
		if (_journeySelectedId == null || _journeyRenameInput == null)
		{
			return;
		}
		try
		{
			_journeyDeleteConfirmationId = null;
			string text = _callbacks.RenameConversationRequested?.Invoke(_journeySelectedId, _journeyRenameInput.text);
			if (text != null && TryReloadJourneys(showError: true))
			{
				JourneyStatus("名称已保存：" + text);
			}
		}
		catch (Exception ex)
		{
			JourneyStatus("保存名称失败：" + ex.Message);
		}
	}

	private void DeleteJourney()
	{
		if (_journeySelectedId == null)
		{
			return;
		}
		ActiveConversationInfo activeConversationInfo = null;
		ActiveConversationInfo activeConversationInfo2 = null;
		foreach (ActiveConversationInfo journeySession in _journeySessions)
		{
			if (string.Equals(journeySession.SessionId, _journeySelectedId, StringComparison.Ordinal))
			{
				activeConversationInfo = journeySession;
			}
			if (journeySession.IsActive)
			{
				activeConversationInfo2 = journeySession;
			}
		}
		if (activeConversationInfo == null)
		{
			JourneyStatus("待删除的幻境已不存在，请重新载入。");
			return;
		}
		if (!string.Equals(_journeyDeleteConfirmationId, activeConversationInfo.SessionId, StringComparison.Ordinal))
		{
			_journeyDeleteConfirmationId = activeConversationInfo.SessionId;
			JourneyStatus("将永久删除“" + Compact(activeConversationInfo.DisplayName, 28) + "”；请再次点击“删除幻境”确认。");
			return;
		}
		try
		{
			string text = _callbacks.DeleteConversationRequested?.Invoke(activeConversationInfo.SessionId, activeConversationInfo2?.SessionId);
			if (text != null)
			{
				_journeyDeleteConfirmationId = null;
				_journeySelectedId = text;
				if (TryReloadJourneys(showError: true))
				{
					JourneyStatus("幻境已删除；当前幻境：" + _journeyActiveName);
				}
			}
		}
		catch (Exception ex)
		{
			_journeyDeleteConfirmationId = null;
			JourneyStatus("删除失败：" + ex.Message);
		}
	}

	private void MoveJourneyPage(int delta)
	{
		_journeyPage += delta;
		RenderJourneyList();
	}

	private void JourneyStatus(string value)
	{
		if (_journeyDialogStatus != null)
		{
			_journeyDialogStatus.text = value;
		}
	}

	[HideFromIl2Cpp]
	internal void AttachSpeechController(MainThreadSpeechController controller)
	{
		_speechController = controller;
	}

	[HideFromIl2Cpp]
	internal void InitializeSpeech(SpeechUiCallbacks callbacks)
	{
		_speechCallbacks = callbacks;
	}

	[HideFromIl2Cpp]
	internal void ApplySpeechState(SpeechSettings settings, string status, bool loaded, TtsServiceState serviceState)
	{
		_lastSpeechSettings = settings;
		_lastServiceState = serviceState;
		_speechLoaded = loaded;
		if (!string.Equals(_lastSpeechStatus, status, StringComparison.Ordinal))
		{
			_lastSpeechStatus = status;
			if (!string.IsNullOrWhiteSpace(status))
			{
				SafeLogInfo("module=TTS; state=ui_status; message=" + SingleLine(status));
			}
		}
		if (!IsStopped)
		{
			_speechRows?.Apply(settings, loaded, serviceState);
		}
	}

	private void MaintainNativeLilithSpeech()
	{
		if (_applicationQuitting || IsStopped)
		{
			return;
		}
		try
		{
			if (!_apiTabSelected && _traySettingsView != null && !(_uiRoot == null))
			{
				object obj = _trayVisible?.GetValue(_traySettingsView);
				if (obj is bool && (bool)obj && Convert.ToInt32(_trayTab?.GetValue(_traySettingsView)) == 3 && _trayItemRoot?.GetValue(_traySettingsView) is Transform transform && !(transform == null) && transform.gameObject.activeInHierarchy)
				{
					if (_speechRows != null && !_speechRows.IsValidFor(transform))
					{
						DetachLilithSpeechRows();
					}
					if (_speechRows == null)
					{
						if (!NativeSpeechLanguageRows.TryCreate(_traySettingsView, transform, _uiRoot.transform, delegate(bool enabled)
						{
							RunUiCallback("SpeechServiceEnabled", delegate
							{
								_speechCallbacks.SelectServiceEnabled?.Invoke(enabled);
							});
						}, delegate(SpeechLanguage language)
						{
							RunUiCallback("SpeechLanguage", delegate
							{
								_speechCallbacks.SelectLanguage?.Invoke(language);
							});
						}, delegate(string? reference)
						{
							RunUiCallback("SpeechReference", delegate
							{
								_speechCallbacks.SelectReference?.Invoke(reference);
							});
						}, out NativeSpeechLanguageRows rows, out string diagnostic))
						{
							ReportSpeechRowsDiagnostic(diagnostic);
							return;
						}
						_speechRows = rows;
						_speechRows.Apply(_lastSpeechSettings, _speechLoaded, _lastServiceState);
						ReportSpeechRowsDiagnostic(diagnostic);
					}
					_speechRows.MaintainPlacement();
					return;
				}
			}
			DetachLilithSpeechRows();
		}
		catch (Exception error)
		{
			DetachLilithSpeechRows();
			SafeLogCallbackError("MaintainNativeLanguageSpeech", error);
		}
	}

	private void DetachLilithSpeechRows()
	{
		NativeSpeechLanguageRows? speechRows = _speechRows;
		_speechRows = null;
		speechRows?.Dispose();
	}

	private void ReportSpeechRowsDiagnostic(string diagnostic)
	{
		if (!string.Equals(_lastSpeechRowsDiagnostic, diagnostic, StringComparison.Ordinal))
		{
			_lastSpeechRowsDiagnostic = diagnostic;
			SafeLogInfo("module=UI; entry=native_language_tts; " + diagnostic);
		}
	}
}
