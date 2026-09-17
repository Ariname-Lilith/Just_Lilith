using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Just_Lilith.Core.Contracts;
using Just_Lilith.Core.Llm;

namespace Just_Lilith.Core.Agent;

public sealed class CodexAgentChannel : IDisposable
{
	private sealed class AgentExecutionCapture
	{
		private readonly CodexAgentChannel _owner;

		private readonly object _sync = new object();

		private readonly Dictionary<string, StringBuilder> _messageDeltas = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);

		private readonly TaskCompletionSource<bool> _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		private readonly string _secret;

		private string _turnId = string.Empty;

		private string _finalReply = string.Empty;

		private string _lastAgentMessage = string.Empty;

		private string _error = string.Empty;

		internal string ThreadId { get; }

		internal Task Completion => _completion.Task;

		internal string FinalReply
		{
			get
			{
				lock (_sync)
				{
					return (_finalReply.Length != 0) ? _finalReply : _lastAgentMessage;
				}
			}
		}

		internal AgentExecutionCapture(CodexAgentChannel owner, string threadId, string? secret)
		{
			_owner = owner;
			ThreadId = threadId;
			_secret = secret ?? string.Empty;
		}

		internal void SetTurnId(string turnId)
		{
			string text = (turnId ?? string.Empty).Trim();
			if (text.Length == 0)
			{
				return;
			}
			lock (_sync)
			{
				_turnId = text;
			}
		}

		internal bool TryGetTurnIdentity(out string threadId, out string turnId)
		{
			lock (_sync)
			{
				threadId = ThreadId;
				turnId = _turnId;
				return threadId.Length != 0 && turnId.Length != 0;
			}
		}

		internal void HandleNotification(string method, JsonElement parameters)
		{
			string text = ReadString(parameters, "threadId", "thread_id");
			if (text.Length != 0 && !string.Equals(text, ThreadId, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			string text2 = ReadString(parameters, "turnId", "turn_id");
			lock (_sync)
			{
				if (_turnId.Length != 0 && text2.Length != 0 && text2 != _turnId)
				{
					return;
				}
			}
			string text3 = NormalizeEventName(method);
			bool flag;
			switch (text3)
			{
			case "turnstarted":
				SetTurnId(ReadNestedString(parameters, "turn", "id"));
				return;
			case "itemagentmessagedelta":
				CaptureDelta(parameters);
				_owner._settings.SetRuntimeStatus("responding");
				return;
			case "itemstarted":
			case "itemcompleted":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag)
			{
				CaptureItem(parameters, text3 == "itemcompleted");
			}
			else if (text3 == "error")
			{
				string text4 = RedactSecret(ReadError(parameters), _secret);
				if (text4.Length != 0)
				{
					lock (_sync)
					{
						_error = text4;
					}
				}
			}
			else if (text3 == "turncompleted")
			{
				CompleteTurn(parameters);
			}
		}

		internal void Fail(Exception exception)
		{
			_completion.TrySetException(exception);
		}

		private void CaptureDelta(JsonElement parameters)
		{
			string text = ReadString(parameters, "itemId", "item_id");
			string text2 = ReadString(parameters, "delta");
			if (text.Length == 0 || text2.Length == 0)
			{
				return;
			}
			lock (_sync)
			{
				if (!_messageDeltas.TryGetValue(text, out StringBuilder value))
				{
					value = new StringBuilder();
					_messageDeltas[text] = value;
				}
				value.Append(text2);
			}
		}

		private void CaptureItem(JsonElement parameters, bool completed)
		{
			if (!parameters.TryGetProperty("item", out var value) || value.ValueKind != JsonValueKind.Object)
			{
				return;
			}
			string text = NormalizeEventName(ReadString(value, "type"));
			if (text == "commandexecution")
			{
				_owner._settings.SetRuntimeStatus("command");
			}
			else if (text == "filechange")
			{
				_owner._settings.SetRuntimeStatus("editing");
			}
			else
			{
				if (text != "agentmessage")
				{
					return;
				}
				_owner._settings.SetRuntimeStatus("responding");
				if (!completed)
				{
					return;
				}
				string text2 = ReadString(value, "text", "message");
				string text3 = ReadString(value, "id", "itemId", "item_id");
				lock (_sync)
				{
					if (text2.Length == 0 && text3.Length != 0 && _messageDeltas.TryGetValue(text3, out StringBuilder value2))
					{
						text2 = value2.ToString();
					}
					if (text2.Length != 0)
					{
						string text4 = NormalizeEventName(ReadString(value, "phase"));
						if (text4 != "commentary")
						{
							_lastAgentMessage = text2;
						}
						if (text4 == "finalanswer")
						{
							_finalReply = text2;
						}
					}
				}
			}
		}

		private void CompleteTurn(JsonElement parameters)
		{
			if (!parameters.TryGetProperty("turn", out var value) || value.ValueKind != JsonValueKind.Object)
			{
				_completion.TrySetException(new AgentProcessException("Codex App Server completed a turn without turn details."));
				return;
			}
			string text = ReadString(value, "id", "turnId", "turn_id");
			lock (_sync)
			{
				if (_turnId.Length != 0 && text.Length != 0 && !string.Equals(_turnId, text, StringComparison.OrdinalIgnoreCase))
				{
					return;
				}
				if (_turnId.Length == 0)
				{
					_turnId = text;
				}
				CaptureAgentMessagesFromTurn(value);
				if (value.TryGetProperty("error", out var value2) && value2.ValueKind == JsonValueKind.Object)
				{
					string text2 = RedactSecret(ReadError(value2), _secret);
					if (text2.Length != 0)
					{
						_error = text2;
					}
				}
			}
			string text3 = NormalizeEventName(ReadString(value, "status"));
			if (text3 == "completed")
			{
				_completion.TrySetResult(result: true);
				return;
			}
			string text4;
			lock (_sync)
			{
				text4 = _error;
			}
			if (text4.Length == 0)
			{
				text4 = ((text3 == "interrupted") ? "The Agent turn was interrupted." : "The Agent turn failed.");
			}
			_completion.TrySetException(new AgentProcessException(text4));
		}

		private void CaptureAgentMessagesFromTurn(JsonElement turn)
		{
			if (!turn.TryGetProperty("items", out var value) || value.ValueKind != JsonValueKind.Array)
			{
				return;
			}
			foreach (JsonElement item in value.EnumerateArray())
			{
				if (item.ValueKind != JsonValueKind.Object || NormalizeEventName(ReadString(item, "type")) != "agentmessage")
				{
					continue;
				}
				string text = ReadString(item, "text", "message");
				if (text.Length != 0)
				{
					if (NormalizeEventName(ReadString(item, "phase")) != "commentary")
					{
						_lastAgentMessage = text;
					}
					if (NormalizeEventName(ReadString(item, "phase")) == "finalanswer")
					{
						_finalReply = text;
					}
				}
			}
		}
	}

	private sealed class AppServerConnection : IDisposable
	{
		private sealed class PendingRequest
		{
			internal string Method { get; }

			internal TaskCompletionSource<JsonElement> Completion { get; } = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

			internal PendingRequest(string method)
			{
				Method = method;
			}
		}

		private readonly Process _process;

		private readonly CodexAgentChannel _owner;

		private readonly object _sync = new object();

		private readonly SemaphoreSlim _writeGate = new SemaphoreSlim(1, 1);

		private readonly Dictionary<long, PendingRequest> _pending = new Dictionary<long, PendingRequest>();

		private readonly string _executable;

		private readonly string _codexHome;

		private readonly string _projectRoot;

		private readonly string _apiKeyVariable;

		private readonly string _providerId;

		private readonly string _baseUrl;

		private readonly string _apiKey;

		private readonly string _model;

		private readonly string _reasoningEffort;

		private readonly bool _usesCustomProvider;

		private long _nextRequestId;

		private int _closed;

		private AgentExecutionCapture? _activeCapture;

		private string _stderrTail = string.Empty;

		internal bool IsAlive
		{
			get
			{
				if (Volatile.Read(ref _closed) != 0)
				{
					return false;
				}
				try
				{
					return !_process.HasExited;
				}
				catch
				{
					return false;
				}
			}
		}

		private AppServerConnection(CodexAgentChannel owner, Process process, string executable, string codexHome, string projectRoot, string apiKeyVariable, AgentProviderConfiguration? provider)
		{
			_owner = owner;
			_process = process;
			_executable = executable;
			_codexHome = codexHome;
			_projectRoot = projectRoot;
			_apiKeyVariable = apiKeyVariable;
			_providerId = provider?.ProviderId ?? string.Empty;
			_usesCustomProvider = provider != null;
			_baseUrl = provider?.BaseUrl ?? string.Empty;
			_apiKey = provider?.ApiKey ?? string.Empty;
			_model = provider?.Model ?? _owner._settings.Model;
			_reasoningEffort = provider?.ReasoningEffort ?? _owner._settings.ReasoningEffort;
		}

		internal static async Task<AppServerConnection> StartAsync(CodexAgentChannel owner, string executable, string codexHome, string projectRoot, string apiKeyVariable, AgentProviderConfiguration? provider, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Process process = new Process
			{
				StartInfo = owner.CreateAppServerStartInfo(executable, codexHome, projectRoot, apiKeyVariable, provider),
				EnableRaisingEvents = true
			};
			if (!process.Start())
			{
				process.Dispose();
				throw new AgentServerDisconnectedException("Codex App Server did not start.");
			}
			AppServerConnection connection = new AppServerConnection(owner, process, executable, codexHome, projectRoot, apiKeyVariable, provider);
			connection.ReadStdoutAsync();
			connection.ReadStderrAsync();
			try
			{
				await connection.SendRequestAsync("initialize", new Dictionary<string, object> { ["clientInfo"] = new Dictionary<string, object>
				{
					["name"] = "lilith-agent",
					["title"] = "莉莉丝 Agent",
					["version"] = "0.3.0"
				} }, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				await connection.SendNotificationAsync("initialized", new Dictionary<string, object>(), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				return connection;
			}
			catch
			{
				connection.Dispose();
				throw;
			}
		}

		internal bool Matches(string executable, string codexHome, string projectRoot, string apiKeyVariable, AgentProviderConfiguration? provider)
		{
			if (string.Equals(_executable, executable, StringComparison.OrdinalIgnoreCase) && string.Equals(_codexHome, codexHome, StringComparison.OrdinalIgnoreCase) && string.Equals(_projectRoot, projectRoot, StringComparison.OrdinalIgnoreCase) && string.Equals(_apiKeyVariable, apiKeyVariable, StringComparison.Ordinal) && _usesCustomProvider == (provider != null) && string.Equals(_providerId, provider?.ProviderId ?? string.Empty, StringComparison.Ordinal) && string.Equals(_baseUrl, provider?.BaseUrl ?? string.Empty, StringComparison.Ordinal) && string.Equals(_apiKey, provider?.ApiKey ?? string.Empty, StringComparison.Ordinal) && string.Equals(_model, provider?.Model ?? _owner._settings.Model, StringComparison.Ordinal))
			{
				return string.Equals(_reasoningEffort, provider?.ReasoningEffort ?? _owner._settings.ReasoningEffort, StringComparison.Ordinal);
			}
			return false;
		}

		internal async Task<JsonElement> SendRequestAsync(string method, object parameters, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			long id = Interlocked.Increment(ref _nextRequestId);
			PendingRequest request = new PendingRequest(method);
			lock (_sync)
			{
				ThrowIfClosed();
				_pending.Add(id, request);
			}
			try
			{
				await WriteMessageAsync(new Dictionary<string, object>
				{
					["id"] = id,
					["method"] = method,
					["params"] = parameters
				}, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				return await WaitForResponseAsync(request.Completion.Task, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			}
			finally
			{
				lock (_sync)
				{
					_pending.Remove(id);
				}
			}
		}

		internal void SetActiveCapture(AgentExecutionCapture capture)
		{
			lock (_sync)
			{
				ThrowIfClosed();
				_activeCapture = capture;
			}
		}

		internal void ClearActiveCapture(AgentExecutionCapture capture)
		{
			lock (_sync)
			{
				if (_activeCapture == capture)
				{
					_activeCapture = null;
				}
			}
		}

		internal async Task InterruptActiveTurnAsync(CancellationToken cancellationToken)
		{
			AgentExecutionCapture capture;
			lock (_sync)
			{
				capture = _activeCapture;
			}
			if (capture == null || !capture.TryGetTurnIdentity(out string threadId, out string turnId))
			{
				return;
			}
			await SendRequestAsync("turn/interrupt", new Dictionary<string, object>
			{
				["threadId"] = threadId,
				["turnId"] = turnId
			}, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			try
			{
				await WaitWithCancellationAsync(capture.Completion, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (AgentProcessException)
			{
			}
		}

		public void Dispose()
		{
			AgentServerDisconnectedException exception = new AgentServerDisconnectedException("Codex App Server was stopped.");
			SignalDisconnected(exception);
			KillProcess(_process);
			try
			{
				_process.Dispose();
			}
			catch
			{
			}
		}

		private async Task SendNotificationAsync(string method, object parameters, CancellationToken cancellationToken)
		{
			await WriteMessageAsync(new Dictionary<string, object>
			{
				["method"] = method,
				["params"] = parameters
			}, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}

		private async Task WriteMessageAsync(object message, CancellationToken cancellationToken)
		{
			await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			try
			{
				ThrowIfClosed();
				string value = JsonSerializer.Serialize(message);
				await _process.StandardInput.WriteLineAsync(value).ConfigureAwait(continueOnCapturedContext: false);
				await _process.StandardInput.FlushAsync().ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception ex) when (((ex is IOException || ex is ObjectDisposedException || ex is InvalidOperationException) ? 1 : 0) != 0)
			{
				AgentServerDisconnectedException ex2 = CreateDisconnectedException(ex);
				SignalDisconnected(ex2);
				throw ex2;
			}
			finally
			{
				_writeGate.Release();
			}
		}

		private async Task ReadStdoutAsync()
		{
			_ = 1;
			try
			{
				string text;
				while ((text = await _process.StandardOutput.ReadLineAsync().ConfigureAwait(continueOnCapturedContext: false)) != null)
				{
					if (text.Length != 0)
					{
						await HandleProtocolLineAsync(text).ConfigureAwait(continueOnCapturedContext: false);
					}
				}
				SignalDisconnected();
			}
			catch (Exception ex) when (((ex is IOException || ex is ObjectDisposedException || ex is InvalidOperationException) ? 1 : 0) != 0)
			{
				SignalDisconnected(CreateDisconnectedException(ex));
			}
		}

		private async Task ReadStderrAsync()
		{
			try
			{
				string value;
				while ((value = await _process.StandardError.ReadLineAsync().ConfigureAwait(continueOnCapturedContext: false)) != null)
				{
					string text = LastUsefulLine(value);
					if (text.Length != 0)
					{
						lock (_sync)
						{
							_stderrTail = RedactSecret(text, _apiKey);
						}
					}
				}
			}
			catch (Exception ex) when (((ex is IOException || ex is ObjectDisposedException || ex is InvalidOperationException) ? 1 : 0) != 0)
			{
			}
		}

		private async Task HandleProtocolLineAsync(string line)
		{
			try
			{
				using JsonDocument document = JsonDocument.Parse(line);
				JsonElement rootElement = document.RootElement;
				if (rootElement.ValueKind != JsonValueKind.Object)
				{
					return;
				}
				if (rootElement.TryGetProperty("id", out var value))
				{
					if (rootElement.TryGetProperty("method", out var value2) && value2.ValueKind == JsonValueKind.String)
					{
						await RespondToUnsupportedServerRequestAsync(value.Clone()).ConfigureAwait(continueOnCapturedContext: false);
					}
					else
					{
						HandleResponse(value, rootElement);
					}
					return;
				}
				string text = ReadString(rootElement, "method");
				if (text.Length == 0)
				{
					return;
				}
				JsonElement parameters = (rootElement.TryGetProperty("params", out var value3) ? value3 : default(JsonElement));
				AgentExecutionCapture activeCapture;
				lock (_sync)
				{
					activeCapture = _activeCapture;
				}
				if (activeCapture != null && parameters.ValueKind == JsonValueKind.Object)
				{
					activeCapture.HandleNotification(text, parameters);
				}
			}
			catch (JsonException)
			{
			}
		}

		private void HandleResponse(JsonElement idElement, JsonElement root)
		{
			if (!TryReadRequestId(idElement, out var id))
			{
				return;
			}
			PendingRequest value;
			lock (_sync)
			{
				if (!_pending.TryGetValue(id, out value))
				{
					return;
				}
				_pending.Remove(id);
			}
			if (root.TryGetProperty("error", out var value2))
			{
				string text = RedactSecret(ReadError(value2), _apiKey);
				if (text.Length == 0)
				{
					text = "Unknown App Server RPC error.";
				}
				value.Completion.TrySetException(new AgentProcessException(value.Method + " failed: " + text));
			}
			else
			{
				JsonElement result = (root.TryGetProperty("result", out var value3) ? value3.Clone() : default(JsonElement));
				value.Completion.TrySetResult(result);
			}
		}

		private async Task RespondToUnsupportedServerRequestAsync(JsonElement id)
		{
			try
			{
				await WriteMessageAsync(new Dictionary<string, object>
				{
					["id"] = id,
					["error"] = new Dictionary<string, object>
					{
						["code"] = -32601,
						["message"] = "This companion channel does not expose interactive server requests."
					}
				}, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (AgentServerDisconnectedException)
			{
			}
		}

		private void SignalDisconnected(Exception? exception = null)
		{
			if (Interlocked.Exchange(ref _closed, 1) == 0)
			{
				AgentServerDisconnectedException exception2 = (exception as AgentServerDisconnectedException) ?? CreateDisconnectedException(exception);
				PendingRequest[] array;
				AgentExecutionCapture activeCapture;
				lock (_sync)
				{
					array = _pending.Values.ToArray();
					_pending.Clear();
					activeCapture = _activeCapture;
					_activeCapture = null;
				}
				PendingRequest[] array2 = array;
				for (int i = 0; i < array2.Length; i++)
				{
					array2[i].Completion.TrySetException(exception2);
				}
				activeCapture?.Fail(exception2);
			}
		}

		private AgentServerDisconnectedException CreateDisconnectedException(Exception? innerException = null)
		{
			string text = string.Empty;
			lock (_sync)
			{
				text = _stderrTail;
			}
			string text2 = string.Empty;
			try
			{
				if (_process.HasExited)
				{
					text2 = " (exit code " + _process.ExitCode + ")";
				}
			}
			catch
			{
			}
			string message = "Codex App Server pipe ended" + text2 + ((text.Length == 0) ? "." : (": " + text));
			if (innerException != null)
			{
				return new AgentServerDisconnectedException(message, innerException);
			}
			return new AgentServerDisconnectedException(message);
		}

		private void ThrowIfClosed()
		{
			if (Volatile.Read(ref _closed) != 0)
			{
				throw CreateDisconnectedException();
			}
		}

		private static bool TryReadRequestId(JsonElement value, out long id)
		{
			if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out id))
			{
				return true;
			}
			if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out id))
			{
				return true;
			}
			id = 0L;
			return false;
		}

		private static async Task<JsonElement> WaitForResponseAsync(Task<JsonElement> response, CancellationToken cancellationToken)
		{
			if (response.IsCompleted)
			{
				return await response.ConfigureAwait(continueOnCapturedContext: false);
			}
			TaskCompletionSource<bool> taskCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			using (cancellationToken.Register(delegate(object? state)
			{
				((TaskCompletionSource<bool>)state).TrySetResult(result: true);
			}, taskCompletionSource))
			{
				if (await Task.WhenAny(response, taskCompletionSource.Task).ConfigureAwait(continueOnCapturedContext: false) != response)
				{
					throw new OperationCanceledException(cancellationToken);
				}
				return await response.ConfigureAwait(continueOnCapturedContext: false);
			}
		}
	}

	private sealed class AgentProviderConfiguration
	{
		internal string ProviderId { get; }

		internal string BaseUrl { get; }

		internal string ApiKey { get; }

		internal string Model { get; }

		internal string ReasoningEffort { get; }

		internal AgentProviderConfiguration(string providerId, string baseUrl, string apiKey, string model, string reasoningEffort)
		{
			ProviderId = providerId;
			BaseUrl = baseUrl;
			ApiKey = apiKey;
			Model = model;
			ReasoningEffort = reasoningEffort;
		}
	}

	private sealed class AgentExecutionResult
	{
		internal string ThreadId { get; }

		internal string FinalReply { get; }

		internal AgentExecutionResult(string threadId, string finalReply)
		{
			ThreadId = threadId;
			FinalReply = finalReply;
		}
	}

	private class AgentProcessException : InvalidOperationException
	{
		internal AgentProcessException(string message)
			: base(message)
		{
		}

		internal AgentProcessException(string message, Exception innerException)
			: base(message, innerException)
		{
		}
	}

	private sealed class AgentServerDisconnectedException : AgentProcessException
	{
		internal AgentServerDisconnectedException(string message)
			: base(message)
		{
		}

		internal AgentServerDisconnectedException(string message, Exception innerException)
			: base(message, innerException)
		{
		}
	}

	private const string LocalProviderSentinel = "agent-local";

	private const string FallbackDesktopProviderId = "Custom";

	private const string AgentThreadName = "莉莉丝 Agent";

	private readonly SemaphoreSlim TurnGate = new SemaphoreSlim(1, 1);

	private readonly object ProcessSync = new object();

	private readonly Action<string>? _log;

	private readonly AgentSettings _settings;

	private readonly Func<AgentProviderInput?>? _provider;

	private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

	private readonly EventHandler _processExit;

	private int _disposed;

	private AppServerConnection? _server;

	private CancellationTokenSource? _activeCancellation;

	private volatile bool _isRunning;

	public bool IsRunning => _isRunning;

	public CodexAgentChannel(AgentSettings settings, Func<AgentProviderInput?>? provider = null, Action<string>? log = null)
	{
		_settings = settings;
		_provider = provider;
		_log = log;
		_processExit = delegate
		{
			Shutdown();
		};
		AppDomain.CurrentDomain.ProcessExit += _processExit;
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 0)
		{
			_lifetime.Cancel();
			Shutdown();
			AppDomain.CurrentDomain.ProcessExit -= _processExit;
		}
	}

	public async Task<string> RequestAsync(string userText, bool japaneseVoiceMode, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (Volatile.Read(ref _disposed) != 0)
		{
			throw new ObjectDisposedException("CodexAgentChannel");
		}
		if (string.IsNullOrWhiteSpace(userText) || userText.Length > 65536)
		{
			throw new ArgumentException("Agent input must be nonempty and at most 65536 characters.");
		}
		using CancellationTokenSource waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
		await TurnGate.WaitAsync(waiting.Token).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			_settings.Reload();
			if (!_settings.Enabled)
			{
				throw new InvalidOperationException("Agent mode was turned off before the request started.");
			}
			waiting.Token.ThrowIfCancellationRequested();
			string agentPersona = _settings.CreatePersonaStore().Load();
			waiting.Token.ThrowIfCancellationRequested();
			string threadId = _settings.ThreadId;
			bool resume = threadId.Length != 0;
			SpeechLanguage language = (japaneseVoiceMode ? SpeechLanguage.Japanese : SpeechLanguage.Chinese);
			string prompt = AgentReply.BuildPrompt(userText, language);
			JsonElement outputSchema = AgentReply.CreateOutputSchema(language);
			_settings.SetRuntimeStatus("starting");
			AgentExecutionResult result;
			try
			{
				result = await ExecuteTurnAsync(prompt, agentPersona, outputSchema, resume ? threadId : null, waiting.Token).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (AgentProcessException ex) when (resume && ex.Message.StartsWith("thread/resume failed:", StringComparison.Ordinal) && LooksLikeMissingThread(ex.Message))
			{
				_settings.ClearThreadId();
				result = await ExecuteTurnAsync(prompt, agentPersona, outputSchema, null, waiting.Token).ConfigureAwait(continueOnCapturedContext: false);
			}
			if (result.ThreadId.Length != 0)
			{
				_settings.SaveThreadId(result.ThreadId);
			}
			if (string.IsNullOrWhiteSpace(result.FinalReply))
			{
				throw new InvalidOperationException("The Agent turn completed without a final reply.");
			}
			AgentReply.Parse(result.FinalReply, language);
			_settings.SetRuntimeStatus("completed");
			_log?.Invoke("Dedicated Agent turn completed for thread " + ShortThreadId(_settings.ThreadId) + ".");
			return result.FinalReply;
		}
		catch (OperationCanceledException)
		{
			_settings.SetRuntimeStatus(_settings.Enabled ? "cancelled" : "disabled");
			throw;
		}
		catch (Exception ex3)
		{
			_settings.SetRuntimeStatus("failed", ex3.Message);
			_log?.Invoke("Dedicated Agent turn failed: " + ex3.GetType().Name + ".");
			throw;
		}
		finally
		{
			TurnGate.Release();
		}
	}

	public void CancelActive()
	{
		AppServerConnection appServerConnection = null;
		CancellationTokenSource activeCancellation;
		lock (ProcessSync)
		{
			activeCancellation = _activeCancellation;
			if (activeCancellation == null)
			{
				appServerConnection = _server;
				_server = null;
			}
		}
		try
		{
			activeCancellation?.Cancel();
		}
		catch
		{
		}
		appServerConnection?.Dispose();
	}

	public void Shutdown()
	{
		AppServerConnection server;
		lock (ProcessSync)
		{
			try
			{
				_activeCancellation?.Cancel();
			}
			catch
			{
			}
			server = _server;
			_server = null;
		}
		server?.Dispose();
	}

	private async Task<AgentExecutionResult> ExecuteTurnAsync(string prompt, string agentPersona, JsonElement outputSchema, string? threadId, CancellationToken cancellationToken)
	{
		string executable = ResolveCodexExecutable();
		string codexHome = ResolveCodexHome();
		string projectRoot = Path.GetFullPath(_settings.ProjectRoot);
		AgentProviderConfiguration provider = ResolveProviderConfiguration(codexHome);
		using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.TimeoutSeconds)))
		{
			using CancellationTokenSource manualCancellation = new CancellationTokenSource();
			using CancellationTokenSource combinedCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, manualCancellation.Token, cancellationToken, _lifetime.Token);
			lock (ProcessSync)
			{
				_activeCancellation = manualCancellation;
				_isRunning = true;
			}
			try
			{
				AgentServerDisconnectedException ex = null;
				for (int attempt = 0; attempt < 2; attempt++)
				{
					combinedCancellation.Token.ThrowIfCancellationRequested();
					AppServerConnection server = null;
					try
					{
						server = await EnsureAppServerAsync(executable, codexHome, projectRoot, provider, combinedCancellation.Token).ConfigureAwait(continueOnCapturedContext: false);
						string threadId2 = ((attempt == 0) ? threadId : _settings.ThreadId);
						return await ExecuteTurnOnServerAsync(server, prompt, agentPersona, outputSchema, threadId2, projectRoot, provider, combinedCancellation.Token).ConfigureAwait(continueOnCapturedContext: false);
					}
					catch (AgentServerDisconnectedException ex2) when (attempt == 0 && !combinedCancellation.IsCancellationRequested)
					{
						ex = ex2;
						DiscardAppServer(server);
						_log?.Invoke("Codex App Server disconnected; restarting it once.");
					}
				}
				throw ex ?? new AgentServerDisconnectedException("Codex App Server disconnected before the Agent turn completed.");
			}
			catch (AgentProcessException innerException) when (combinedCancellation.IsCancellationRequested)
			{
				await InterruptAndDiscardAppServerAsync().ConfigureAwait(continueOnCapturedContext: false);
				if (manualCancellation.IsCancellationRequested || cancellationToken.IsCancellationRequested || _lifetime.IsCancellationRequested || !_settings.Enabled)
				{
					throw new OperationCanceledException("The Agent turn was cancelled.", innerException, manualCancellation.Token);
				}
				throw new TimeoutException("The Agent turn exceeded " + _settings.TimeoutSeconds + " seconds.", innerException);
			}
			catch (OperationCanceledException innerException2)
			{
				await InterruptAndDiscardAppServerAsync().ConfigureAwait(continueOnCapturedContext: false);
				if (manualCancellation.IsCancellationRequested || cancellationToken.IsCancellationRequested || _lifetime.IsCancellationRequested || !_settings.Enabled)
				{
					throw new OperationCanceledException("The Agent turn was cancelled.", innerException2, manualCancellation.Token);
				}
				throw new TimeoutException("The Agent turn exceeded " + _settings.TimeoutSeconds + " seconds.", innerException2);
			}
			finally
			{
				lock (ProcessSync)
				{
					if (_activeCancellation == manualCancellation)
					{
						_activeCancellation = null;
						_isRunning = false;
					}
				}
			}
		}
		IL_0606:
		throw null;
	}

	private async Task<AgentExecutionResult> ExecuteTurnOnServerAsync(AppServerConnection server, string prompt, string agentPersona, JsonElement outputSchema, string? threadId, string projectRoot, AgentProviderConfiguration? provider, CancellationToken cancellationToken)
	{
		string activeThreadId = (threadId ?? string.Empty).Trim();
		string model = provider?.Model ?? _settings.Model;
		if (activeThreadId.Length == 0)
		{
			Dictionary<string, object> dictionary = new Dictionary<string, object>
			{
				["cwd"] = projectRoot,
				["threadSource"] = "user",
				["ephemeral"] = false,
				["model"] = (string.IsNullOrWhiteSpace(model) ? null : model),
				["sandbox"] = "workspace-write",
				["approvalPolicy"] = "never",
				["developerInstructions"] = agentPersona
			};
			if (provider != null)
			{
				dictionary["modelProvider"] = provider.ProviderId;
			}
			activeThreadId = ReadNestedString(await server.SendRequestAsync("thread/start", dictionary, cancellationToken).ConfigureAwait(continueOnCapturedContext: false), "thread", "id");
			if (activeThreadId.Length == 0)
			{
				throw new AgentProcessException("Codex App Server created a thread without returning its id.");
			}
			_settings.SaveThreadId(activeThreadId);
		}
		else
		{
			Dictionary<string, object> dictionary2 = new Dictionary<string, object>
			{
				["threadId"] = activeThreadId,
				["cwd"] = projectRoot,
				["excludeTurns"] = true,
				["model"] = (string.IsNullOrWhiteSpace(model) ? null : model),
				["sandbox"] = "workspace-write",
				["approvalPolicy"] = "never",
				["developerInstructions"] = agentPersona
			};
			if (provider != null)
			{
				dictionary2["modelProvider"] = provider.ProviderId;
			}
			string text = ReadNestedString(await server.SendRequestAsync("thread/resume", dictionary2, cancellationToken).ConfigureAwait(continueOnCapturedContext: false), "thread", "id");
			if (text.Length != 0)
			{
				activeThreadId = text;
			}
			_settings.SaveThreadId(activeThreadId);
		}
		await server.SendRequestAsync("thread/name/set", new Dictionary<string, object>
		{
			["threadId"] = activeThreadId,
			["name"] = "莉莉丝 Agent"
		}, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		AgentExecutionCapture capture = new AgentExecutionCapture(this, activeThreadId, provider?.ApiKey);
		server.SetActiveCapture(capture);
		bool turnSubmissionAttempted = false;
		AgentExecutionResult result;
		try
		{
			int num = 0;
			_ = num - 3;
			_ = 1;
			try
			{
				_settings.SetRuntimeStatus("working");
				turnSubmissionAttempted = true;
				capture.SetTurnId(ReadNestedString(await server.SendRequestAsync("turn/start", new Dictionary<string, object>
				{
					["threadId"] = activeThreadId,
					["input"] = new object[1]
					{
						new Dictionary<string, object>
						{
							["type"] = "text",
							["text"] = prompt
						}
					},
					["cwd"] = projectRoot,
					["model"] = (string.IsNullOrWhiteSpace(model) ? null : model),
					["effort"] = provider?.ReasoningEffort ?? _settings.ReasoningEffort,
					["outputSchema"] = outputSchema,
					["approvalPolicy"] = "never"
				}, cancellationToken).ConfigureAwait(continueOnCapturedContext: false), "turn", "id"));
				await WaitWithCancellationAsync(capture.Completion, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				if (capture.FinalReply.Length == 0)
				{
					throw new AgentProcessException("The Agent event stream contained no final assistant message.");
				}
				result = new AgentExecutionResult(activeThreadId, capture.FinalReply);
			}
			catch (AgentServerDisconnectedException innerException) when (turnSubmissionAttempted)
			{
				throw new AgentProcessException("The Codex connection ended after the Agent turn was submitted. Its work may already be recorded in the Codex thread; open that thread before sending the request again.", innerException);
			}
		}
		finally
		{
			if (cancellationToken.IsCancellationRequested)
			{
				await InterruptAndDiscardAppServerAsync().ConfigureAwait(continueOnCapturedContext: false);
			}
			server.ClearActiveCapture(capture);
		}
		return result;
	}

	private async Task<AppServerConnection> EnsureAppServerAsync(string executable, string codexHome, string projectRoot, AgentProviderConfiguration? provider, CancellationToken cancellationToken)
	{
		AppServerConnection server;
		lock (ProcessSync)
		{
			server = _server;
			if (server != null && server.IsAlive && server.Matches(executable, codexHome, projectRoot, _settings.ApiKeyEnvironmentVariable, provider))
			{
				return server;
			}
			_server = null;
		}
		server?.Dispose();
		AppServerConnection appServerConnection = await AppServerConnection.StartAsync(this, executable, codexHome, projectRoot, _settings.ApiKeyEnvironmentVariable, provider, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		bool flag = false;
		try
		{
			lock (ProcessSync)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (_server == null)
				{
					_server = appServerConnection;
					flag = true;
					return appServerConnection;
				}
				server = _server;
			}
			return server;
		}
		finally
		{
			if (!flag)
			{
				appServerConnection.Dispose();
			}
		}
	}

	private void DiscardAppServer(AppServerConnection? expected = null)
	{
		AppServerConnection appServerConnection = null;
		lock (ProcessSync)
		{
			if (_server != null && (expected == null || _server == expected))
			{
				appServerConnection = _server;
				_server = null;
			}
		}
		appServerConnection?.Dispose();
	}

	private async Task InterruptAndDiscardAppServerAsync()
	{
		AppServerConnection server;
		lock (ProcessSync)
		{
			server = _server;
		}
		if (server != null)
		{
			using CancellationTokenSource cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3.0));
			try
			{
				await server.InterruptActiveTurnAsync(cleanupTimeout.Token).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception ex) when (((ex is OperationCanceledException || ex is AgentProcessException || ex is ObjectDisposedException) ? 1 : 0) != 0)
			{
			}
		}
		DiscardAppServer(server);
	}

	private ProcessStartInfo CreateAppServerStartInfo(string executable, string codexHome, string projectRoot, string apiKeyVariable, AgentProviderConfiguration? provider)
	{
		ProcessStartInfo processStartInfo = new ProcessStartInfo
		{
			FileName = executable,
			WorkingDirectory = projectRoot,
			UseShellExecute = false,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
			WindowStyle = ProcessWindowStyle.Hidden,
			StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
			StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
			StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
		};
		AddArgument(processStartInfo, "app-server");
		AddArgument(processStartInfo, "--listen");
		AddArgument(processStartInfo, "stdio://");
		if (provider != null)
		{
			string text = "model_providers." + provider.ProviderId;
			AddConfigArgument(processStartInfo, "model_provider", provider.ProviderId);
			AddConfigArgument(processStartInfo, text + ".name", "Lilith Custom API");
			AddConfigArgument(processStartInfo, text + ".base_url", provider.BaseUrl);
			AddConfigArgument(processStartInfo, text + ".env_key", apiKeyVariable);
			AddConfigArgument(processStartInfo, text + ".wire_api", "responses");
		}
		if (provider != null)
		{
			AddArgument(processStartInfo, "-c");
			AddArgument(processStartInfo, "shell_environment_policy.exclude=[" + TomlString(apiKeyVariable) + "]");
		}
		processStartInfo.Environment["CODEX_HOME"] = codexHome;
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (folderPath.Length != 0)
		{
			processStartInfo.Environment["HOME"] = folderPath;
		}
		string directoryName = Path.GetDirectoryName(executable);
		if (!string.IsNullOrWhiteSpace(directoryName))
		{
			processStartInfo.Environment["PATH"] = directoryName + Path.PathSeparator + (processStartInfo.Environment.TryGetValue("PATH", out string value) ? value : string.Empty);
		}
		if (provider != null)
		{
			processStartInfo.Environment[apiKeyVariable] = provider.ApiKey;
		}
		return processStartInfo;
	}

	private static void AddArgument(ProcessStartInfo info, string value)
	{
		info.ArgumentList.Add(value);
	}

	private static void AddConfigArgument(ProcessStartInfo info, string key, string value)
	{
		AddArgument(info, "-c");
		AddArgument(info, key + "=" + TomlString(value));
	}

	private static string TomlString(string value)
	{
		return JsonSerializer.Serialize(value ?? string.Empty);
	}

	private AgentProviderConfiguration? ResolveProviderConfiguration(string codexHome)
	{
		if (_settings.Options.ProviderMode == "codex")
		{
			return null;
		}
		AgentProviderInput agentProviderInput = _provider?.Invoke();
		if (agentProviderInput == null)
		{
			if (_settings.Options.ProviderMode == "saved_responses")
			{
				throw new InvalidOperationException("请先保存顶部 Responses 配置，或选择 codex 提供方模式。");
			}
			return null;
		}
		if (agentProviderInput.Protocol != LlmApiFormat.Responses)
		{
			if (_settings.Options.ProviderMode == "auto")
			{
				return null;
			}
			throw new InvalidOperationException("Agent 自定义提供方要求 Responses 协议。");
		}
		return new AgentProviderConfiguration(ResolveDesktopProviderId(codexHome, agentProviderInput.BaseUrl, _settings.ApiKeyEnvironmentVariable), agentProviderInput.BaseUrl, agentProviderInput.ApiKey, agentProviderInput.Model, agentProviderInput.ReasoningEffort);
	}

	private string ResolveDesktopProviderId(string codexHome, string expectedBaseUrl, string expectedEnvironmentKey)
	{
		string path = Path.Combine(codexHome, "config.toml");
		if (!File.Exists(path))
		{
			throw new InvalidOperationException("Agent Custom API requires an existing Responses provider in Codex config.toml.");
		}
		string text = string.Empty;
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
		Dictionary<string, string> dictionary2 = new Dictionary<string, string>(StringComparer.Ordinal);
		Dictionary<string, string> dictionary3 = new Dictionary<string, string>(StringComparer.Ordinal);
		string text2 = null;
		bool flag = true;
		try
		{
			foreach (string item in File.ReadLines(path, Encoding.UTF8))
			{
				string text3 = RemoveTomlComment(item).Trim();
				if (text3.Length == 0)
				{
					continue;
				}
				if (text3[0] == '[')
				{
					flag = false;
					text2 = ReadProviderTableId(text3);
				}
				else if (flag)
				{
					string text4 = ReadTomlAssignment(text3, "model_provider");
					if (text4.Length != 0)
					{
						text = text4;
					}
				}
				else if (text2 != null)
				{
					string text5 = ReadTomlAssignment(text3, "wire_api");
					if (text5.Length != 0)
					{
						dictionary[text2] = text5;
					}
					string text6 = ReadTomlAssignment(text3, "base_url");
					if (text6.Length != 0)
					{
						dictionary2[text2] = text6;
					}
					string text7 = ReadTomlAssignment(text3, "env_key");
					if (text7.Length != 0)
					{
						dictionary3[text2] = text7;
					}
				}
			}
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			throw new InvalidOperationException("Codex config.toml could not be read for Agent provider selection.", ex);
		}
		string codexModelProvider = _settings.CodexModelProvider;
		string text8 = ((codexModelProvider.Length != 0) ? codexModelProvider : ((text.Length != 0) ? text : "Custom"));
		if (!IsSafeProviderId(text8))
		{
			throw new InvalidOperationException("Agent.CodexModelProvider must contain only letters, digits, underscore, or hyphen.");
		}
		if (!dictionary.TryGetValue(text8, out var value))
		{
			throw new InvalidOperationException("Agent provider '" + text8 + "' is not defined in Codex config.toml. Define it there or select an existing provider with Agent.CodexModelProvider.");
		}
		if (!string.Equals(value, "responses", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Agent provider '" + text8 + "' must use wire_api = \"responses\" in Codex config.toml.");
		}
		if (!dictionary2.TryGetValue(text8, out var value2) || !string.Equals(value2.TrimEnd('/'), expectedBaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Agent provider '" + text8 + "' has a different base_url in Codex config.toml. Select a provider that matches the saved Custom API endpoint.");
		}
		if (!dictionary3.TryGetValue(text8, out var value3) || !string.Equals(value3, expectedEnvironmentKey, StringComparison.Ordinal))
		{
			throw new InvalidOperationException("Agent provider '" + text8 + "' has a different env_key in Codex config.toml. Match it to Agent.ApiKeyEnvironmentVariable before using Agent mode.");
		}
		return text8;
	}

	private static string? ReadProviderTableId(string line)
	{
		if (!line.StartsWith("[model_providers.", StringComparison.Ordinal) || !line.EndsWith("]", StringComparison.Ordinal))
		{
			return null;
		}
		string text = line.Substring("[model_providers.".Length, line.Length - "[model_providers.".Length - 1).Trim();
		if (text.IndexOf('.') >= 0)
		{
			return null;
		}
		text = ParseTomlScalar(text);
		if (!IsSafeProviderId(text))
		{
			return null;
		}
		return text;
	}

	private static string ReadTomlAssignment(string line, string expectedKey)
	{
		int num = line.IndexOf('=');
		if (num <= 0 || !string.Equals(line.Substring(0, num).Trim(), expectedKey, StringComparison.Ordinal))
		{
			return string.Empty;
		}
		return ParseTomlScalar(line.Substring(num + 1).Trim());
	}

	private static string ParseTomlScalar(string value)
	{
		string text = (value ?? string.Empty).Trim();
		if (text.Length >= 2 && text[0] == '\'')
		{
			if (text[text.Length - 1] == '\'')
			{
				return text.Substring(1, text.Length - 2);
			}
		}
		if (text.Length >= 2 && text[0] == '"')
		{
			if (text[text.Length - 1] == '"')
			{
				try
				{
					return JsonSerializer.Deserialize<string>(text) ?? string.Empty;
				}
				catch (JsonException)
				{
					return string.Empty;
				}
			}
		}
		return text;
	}

	private static string RemoveTomlComment(string value)
	{
		bool flag = false;
		bool flag2 = false;
		bool flag3 = false;
		for (int i = 0; i < value.Length; i++)
		{
			char c = value[i];
			if (flag)
			{
				if (flag3)
				{
					flag3 = false;
					continue;
				}
				switch (c)
				{
				case '\\':
					flag3 = true;
					break;
				case '"':
					flag = false;
					break;
				}
			}
			else if (flag2)
			{
				if (c == '\'')
				{
					flag2 = false;
				}
			}
			else
			{
				switch (c)
				{
				case '"':
					flag = true;
					break;
				case '\'':
					flag2 = true;
					break;
				case '#':
					return value.Substring(0, i);
				}
			}
		}
		return value;
	}

	private static bool IsSafeProviderId(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		foreach (char c in value)
		{
			bool flag = char.IsLetterOrDigit(c);
			if (!flag)
			{
				bool flag2 = ((c == '-' || c == '_') ? true : false);
				flag = flag2;
			}
			if (!flag)
			{
				return false;
			}
		}
		return true;
	}

	private static string ReadNestedString(JsonElement root, string objectName, params string[] names)
	{
		if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(objectName, out var value) || value.ValueKind != JsonValueKind.Object)
		{
			return string.Empty;
		}
		return ReadString(value, names);
	}

	private static async Task WaitWithCancellationAsync(Task task, CancellationToken cancellationToken)
	{
		if (task.IsCompleted)
		{
			await task.ConfigureAwait(continueOnCapturedContext: false);
			return;
		}
		TaskCompletionSource<bool> taskCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using (cancellationToken.Register(delegate(object? state)
		{
			((TaskCompletionSource<bool>)state).TrySetResult(result: true);
		}, taskCompletionSource))
		{
			if (await Task.WhenAny(task, taskCompletionSource.Task).ConfigureAwait(continueOnCapturedContext: false) != task)
			{
				throw new OperationCanceledException(cancellationToken);
			}
			await task.ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	private string ResolveCodexExecutable()
	{
		string text = ResolveOptionalPath(_settings.ConfiguredExecutable, _settings.ProjectRoot);
		if (_settings.ConfiguredExecutable.Length != 0)
		{
			if (text.Length == 0 || !File.Exists(text))
			{
				throw new FileNotFoundException("配置的 codex.exe 路径不存在。");
			}
			return text;
		}
		string text2 = ResolveOptionalPath(Environment.GetEnvironmentVariable("CODEX_EXECUTABLE") ?? string.Empty, _settings.ProjectRoot);
		if (text2.Length != 0 && File.Exists(text2))
		{
			return text2;
		}
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (folderPath.Length != 0)
		{
			string text3 = Path.Combine(folderPath, ".codex", "packages", "standalone", "current", "bin", "codex.exe");
			if (File.Exists(text3))
			{
				return text3;
			}
		}
		string folderPath2 = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		if (folderPath2.Length != 0)
		{
			string text4 = Path.Combine(folderPath2, "Programs", "OpenAI", "Codex", "bin", "codex.exe");
			if (File.Exists(text4))
			{
				return text4;
			}
			string path = Path.Combine(folderPath2, "OpenAI", "Codex", "bin");
			if (Directory.Exists(path))
			{
				string text5 = (from directory in Directory.EnumerateDirectories(path)
					select Path.Combine(directory, "codex.exe")).Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
				if (!string.IsNullOrWhiteSpace(text5))
				{
					return text5;
				}
			}
		}
		string[] array = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
		foreach (string text6 in array)
		{
			try
			{
				string text7 = Path.Combine(text6.Trim().Trim('"'), "codex.exe");
				if (File.Exists(text7))
				{
					return text7;
				}
			}
			catch
			{
			}
		}
		throw new FileNotFoundException("codex.exe was not found. Install Codex locally or set Agent.CodexExecutable in the plugin configuration.");
	}

	private string ResolveCodexHome()
	{
		string text = ResolveOptionalPath(_settings.ConfiguredCodexHome, _settings.ProjectRoot);
		if (_settings.ConfiguredCodexHome.Length != 0)
		{
			if (text.Length == 0 || !Directory.Exists(text))
			{
				throw new DirectoryNotFoundException("配置的 CodexHome 路径不存在。");
			}
			return text;
		}
		string text2 = ResolveOptionalPath(Environment.GetEnvironmentVariable("CODEX_HOME") ?? string.Empty, _settings.ProjectRoot);
		if (text2.Length != 0 && Directory.Exists(text2))
		{
			return text2;
		}
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		string text3 = ((folderPath.Length == 0) ? string.Empty : Path.Combine(folderPath, ".codex"));
		if (text3.Length != 0 && Directory.Exists(text3))
		{
			return text3;
		}
		throw new DirectoryNotFoundException("The local Codex home directory was not found. Sign in to Codex or set Agent.CodexHome in the plugin configuration.");
	}

	private static string ResolveOptionalPath(string value, string relativeRoot)
	{
		string text = Environment.ExpandEnvironmentVariables((value ?? string.Empty).Trim().Trim('"'));
		if (text.Length == 0)
		{
			return string.Empty;
		}
		try
		{
			return Path.GetFullPath(Path.IsPathRooted(text) ? text : Path.Combine(relativeRoot, text));
		}
		catch
		{
			return string.Empty;
		}
	}

	private static string NormalizeEventName(string value)
	{
		StringBuilder stringBuilder = new StringBuilder(value.Length);
		foreach (char c in value)
		{
			if (char.IsLetterOrDigit(c))
			{
				stringBuilder.Append(char.ToLowerInvariant(c));
			}
		}
		return stringBuilder.ToString();
	}

	private static string ReadString(JsonElement value, params string[] names)
	{
		if (value.ValueKind != JsonValueKind.Object)
		{
			return string.Empty;
		}
		foreach (string propertyName in names)
		{
			if (value.TryGetProperty(propertyName, out var value2) && value2.ValueKind == JsonValueKind.String)
			{
				return value2.GetString() ?? string.Empty;
			}
		}
		return string.Empty;
	}

	private static string ReadError(JsonElement root)
	{
		string text = ReadString(root, "message", "error");
		if (text.Length != 0)
		{
			return text;
		}
		if (root.TryGetProperty("error", out var value) && value.ValueKind == JsonValueKind.Object)
		{
			return ReadString(value, "message", "detail");
		}
		return string.Empty;
	}

	private static string LastUsefulLine(string value)
	{
		string[] array = (value ?? string.Empty).Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		string text = ((array.Length == 0) ? string.Empty : array[^1].Trim());
		if (text.Length > 400)
		{
			return text.Substring(0, 400) + "…";
		}
		return text;
	}

	private static string RedactSecret(string value, string? secret)
	{
		string text = value ?? string.Empty;
		if (!string.IsNullOrEmpty(secret))
		{
			text = text.Replace(secret, "<redacted>", StringComparison.Ordinal);
		}
		return text;
	}

	private static bool LooksLikeMissingThread(string value)
	{
		string text = (value ?? string.Empty).ToLowerInvariant();
		if (text.Contains("thread"))
		{
			if (!text.Contains("not found") && !text.Contains("no rollout") && !text.Contains("does not exist"))
			{
				return text.Contains("unknown session");
			}
			return true;
		}
		return false;
	}

	private static string ShortThreadId(string value)
	{
		string text = (value ?? string.Empty).Trim();
		if (text.Length > 12)
		{
			return text.Substring(0, 12);
		}
		return text;
	}

	private static void KillProcess(Process? process)
	{
		if (process == null)
		{
			return;
		}
		try
		{
			if (!process.HasExited)
			{
				process.Kill(entireProcessTree: true);
			}
		}
		catch
		{
		}
	}
}
