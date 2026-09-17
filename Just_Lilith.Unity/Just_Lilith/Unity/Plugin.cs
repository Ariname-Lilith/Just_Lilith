using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Just_Lilith.Core.Configuration;
using Just_Lilith.Core.Speech;
using Just_Lilith.Core.Threading;
using Just_Lilith.Unity.Game;
using Just_Lilith.Unity.Speech;
using Just_Lilith.Unity.Ui;

namespace Just_Lilith.Unity;

[BepInPlugin("local.just_lilith", "Just_Lilith", "0.3.0")]
public sealed class Plugin : BasePlugin
{
	public const string Id = "local.just_lilith";

	public const string Name = "Just_Lilith";

	public const string Version = "0.3.0";

	private MainThreadPump? _pump;

	private LlmConfigurationView? _view;

	private LlmConfigurationController? _configuration;

	private LlmGameBridge? _gameBridge;

	private PetStateSnapshotReader? _petState;

	private MainThreadSpeechController? _speech;

	private CancellationTokenSource? _lifetime;

	private Task? _startup;

	public Task<IMainThreadDispatcher> MainThreadReady { get; private set; } = Task.FromCanceled<IMainThreadDispatcher>(new CancellationToken(canceled: true));

	public override void Load()
	{
		if ((object)_pump != null)
		{
			throw new InvalidOperationException("Plugin already loaded.");
		}
		string text = Path.Combine(Paths.ConfigPath, "local.just_lilith.json");
		FoundationOptions foundationOptions;
		bool isEnabled;
		try
		{
			foundationOptions = FoundationOptionsStore.LoadOrCreate(text);
		}
		catch (Exception ex)
		{
			ManualLogSource log = base.Log;
			BepInExErrorLogInterpolatedStringHandler bepInExErrorLogInterpolatedStringHandler = new BepInExErrorLogInterpolatedStringHandler(67, 1, out isEnabled);
			if (isEnabled)
			{
				bepInExErrorLogInterpolatedStringHandler.AppendLiteral("foundation configuration error: ");
				bepInExErrorLogInterpolatedStringHandler.AppendFormatted(ex.GetType().Name);
				bepInExErrorLogInterpolatedStringHandler.AppendLiteral("; existing configuration preserved.");
			}
			log.LogError(bepInExErrorLogInterpolatedStringHandler);
			throw;
		}
		BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler;
		if (!foundationOptions.Enabled)
		{
			ManualLogSource log2 = base.Log;
			bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(37, 2, out isEnabled);
			if (isEnabled)
			{
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted("Just_Lilith");
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(" ");
				bepInExInfoLogInterpolatedStringHandler.AppendFormatted("0.3.0");
				bepInExInfoLogInterpolatedStringHandler.AppendLiteral(": disabled by its own configuration.");
			}
			log2.LogInfo(bepInExInfoLogInterpolatedStringHandler);
			return;
		}
		try
		{
			_lifetime = new CancellationTokenSource();
			_pump = AddComponent<MainThreadPump>();
			_pump.Configure(base.Log);
			MainThreadReady = _pump.GetReadyTask();
			_startup = InitializeModulesAsync(_lifetime.Token);
		}
		catch
		{
			_lifetime?.Cancel();
			_pump?.RequestStop("initialization_failure");
			_pump = null;
			throw;
		}
		ManualLogSource log3 = base.Log;
		bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(44, 3, out isEnabled);
		if (isEnabled)
		{
			bepInExInfoLogInterpolatedStringHandler.AppendFormatted("Just_Lilith");
			bepInExInfoLogInterpolatedStringHandler.AppendLiteral(" ");
			bepInExInfoLogInterpolatedStringHandler.AppendFormatted("0.3.0");
			bepInExInfoLogInterpolatedStringHandler.AppendLiteral(": independent plugin loaded; configuration=");
			bepInExInfoLogInterpolatedStringHandler.AppendFormatted(text);
		}
		log3.LogInfo(bepInExInfoLogInterpolatedStringHandler);
		base.Log.LogInfo("module=MainThread; state=waiting_for_update");
		base.Log.LogInfo("module=UI; state=waiting_for_mainthread");
		base.Log.LogInfo("module=LLM; state=waiting_for_configuration");
		base.Log.LogInfo("module=TTS; state=waiting_for_saved_service_preference; first_default=off; lifecycle=plugin_owned");
		base.Log.LogInfo("module=GameBridge; state=waiting_for_runtime_objects; bubble=DialogueManager_reflection; keyboard=TransparentWindowNew_reflection");
		if (foundationOptions.VerboseLogging)
		{
			base.Log.LogInfo("LLM requests require explicit actions; local TTS starts only when its saved service preference is enabled.");
		}
	}

	private async Task InitializeModulesAsync(CancellationToken cancellationToken)
	{
		try
		{
			IMainThreadDispatcher dispatcher = await MainThreadReady.ConfigureAwait(continueOnCapturedContext: false);
			await dispatcher.Post(delegate
			{
				cancellationToken.ThrowIfCancellationRequested();
				_gameBridge = new LlmGameBridge(base.Log);
				_petState = new PetStateSnapshotReader(base.Log);
				_view = AddComponent<LlmConfigurationView>();
				_view.AttachLogger(base.Log);
				_view.AttachGameBridge(_gameBridge);
				try
				{
					_speech = new MainThreadSpeechController(_gameBridge, _view, Path.Combine(Paths.ConfigPath, "local.just_lilith.tts.json"), base.Log, new TtsServiceManager(new TtsServiceOptions(TtsRuntimeLocation.Resolve())));
					_view.AttachSpeechController(_speech);
					_speech.Initialize();
				}
				catch (Exception ex3)
				{
					_speech?.RequestStop();
					_speech = null;
					_view.ApplySpeechState(new SpeechSettings(), "TTS 初始化失败；聊天继续可用，请查看插件日志。", loaded: false, new TtsServiceState(TtsServicePhase.Failed, "TTS 初始化失败"));
					base.Log.LogWarning("module=TTS; state=initialization_failed; chat_preserved=true; error=" + ex3.GetType().Name);
				}
				_configuration = new LlmConfigurationController(dispatcher, _view, Path.Combine(Paths.ConfigPath, "local.just_lilith.llm.json"), Path.Combine(Paths.ConfigPath, "local.just_lilith.workspace"), base.Log, _gameBridge, _petState, _speech, Paths.GameRootPath);
				_configuration.Initialize();
			}, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			_speech?.RequestStop();
			_configuration?.Dispose();
			_view?.RequestStop();
			_gameBridge?.Dispose();
			ManualLogSource log = base.Log;
			BepInExErrorLogInterpolatedStringHandler bepInExErrorLogInterpolatedStringHandler = new BepInExErrorLogInterpolatedStringHandler(31, 1, out var isEnabled);
			if (isEnabled)
			{
				bepInExErrorLogInterpolatedStringHandler.AppendLiteral("module=UI; state=failed; error=");
				bepInExErrorLogInterpolatedStringHandler.AppendFormatted(ex2.GetType().Name);
			}
			log.LogError(bepInExErrorLogInterpolatedStringHandler);
		}
	}

	public override bool Unload()
	{
		_lifetime?.Cancel();
		_speech?.RequestStop();
		_configuration?.Dispose();
		_view?.RequestStop();
		_gameBridge?.Dispose();
		_configuration = null;
		_view = null;
		_gameBridge = null;
		_petState = null;
		_speech = null;
		_pump?.RequestStop("plugin_unload");
		_pump = null;
		MainThreadReady = Task.FromCanceled<IMainThreadDispatcher>(new CancellationToken(canceled: true));
		ManualLogSource log = base.Log;
		BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(18, 1, out var isEnabled);
		if (isEnabled)
		{
			bepInExInfoLogInterpolatedStringHandler.AppendFormatted("Just_Lilith");
			bepInExInfoLogInterpolatedStringHandler.AppendLiteral(": plugin unloaded.");
		}
		log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
		return true;
	}
}
