using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using Just_Lilith.Core.Agent;

namespace Just_Lilith.AgentNative;

internal static class AgentCatalog
{
	private static readonly object Sync = new object();

	private static CatalogSnapshot _snapshot = Empty();

	private static CodexAgentChannel? _channel;

	private static string _configurationKey = "";

	private static bool _enabled;

	private static long _generation;

	private static Task? _refresh;

	private static CancellationTokenSource? _cancellation;

	public static CatalogSnapshot Get(CodexAgentChannel channel, AgentSettings settings)
	{
		bool enabled = settings.Enabled;
		string text = (enabled ? ConfigurationKey(channel, settings) : "");
		lock (Sync)
		{
			if (channel != _channel || enabled != _enabled || !string.Equals(text, _configurationKey, StringComparison.Ordinal))
			{
				_cancellation?.Cancel();
				_cancellation = null;
				_refresh = null;
				_snapshot = Empty();
				_channel = channel;
				_configurationKey = text;
				_enabled = enabled;
				long generation = ++_generation;
				if (enabled)
				{
					CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15.0));
					_cancellation = cancellation;
					_refresh = Task.Run(() => RefreshAsync(channel, settings.ProjectRoot, generation, cancellation));
				}
			}
			return _snapshot;
		}
	}

	private static string ConfigurationKey(CodexAgentChannel channel, AgentSettings settings)
	{
		object providerInput = null;
		if (!string.Equals(settings.Options.ProviderMode, "codex", StringComparison.Ordinal))
		{
			Delegate obj = AccessTools.Field(typeof(CodexAgentChannel), "_provider")?.GetValue(channel) as Delegate;
			try
			{
				providerInput = obj?.DynamicInvoke();
			}
			catch
			{
			}
		}
		return string.Join("\n", settings.Options.ProviderMode, settings.ConfiguredExecutable, settings.ConfiguredCodexHome, settings.CodexModelProvider, settings.ApiKeyEnvironmentVariable, settings.ProjectRoot, ProviderValue("BaseUrl"), ProviderValue("ApiKey"), ProviderValue("Protocol"));
		string ProviderValue(string property)
		{
			return providerInput?.GetType().GetProperty(property)?.GetValue(providerInput)?.ToString() ?? "";
		}
	}

	private static async Task RefreshAsync(CodexAgentChannel channel, string projectRoot, long generation, CancellationTokenSource cancellation)
	{
		_ = 1;
		try
		{
			CancellationToken token = cancellation.Token;
			token.ThrowIfCancellationRequested();
			Type typeFromHandle = typeof(CodexAgentChannel);
			string text = (string)AccessTools.Method(typeFromHandle, "ResolveCodexExecutable").Invoke(channel, null);
			string text2 = (string)AccessTools.Method(typeFromHandle, "ResolveCodexHome").Invoke(channel, null);
			object obj = AccessTools.Method(typeFromHandle, "ResolveProviderConfiguration").Invoke(channel, new object[1] { text2 });
			MethodInfo methodInfo = AccessTools.Method(typeFromHandle, "EnsureAppServerAsync");
			Task serverTask = (Task)methodInfo.Invoke(channel, new object[5] { text, text2, projectRoot, obj, token });
			await serverTask.WaitAsync(token).ConfigureAwait(continueOnCapturedContext: false);
			object server = serverTask.GetType().GetProperty("Result").GetValue(serverTask);
			MethodInfo send = AccessTools.Method(server.GetType(), "SendRequestAsync");
			Dictionary<string, CatalogModel> models = new Dictionary<string, CatalogModel>(StringComparer.Ordinal);
			HashSet<string> seenCursors = new HashSet<string>(StringComparer.Ordinal);
			string text3 = null;
			do
			{
				token.ThrowIfCancellationRequested();
				Task requestTask = (Task)send.Invoke(server, new object[3]
				{
					"model/list",
					new
					{
						cursor = text3,
						limit = 100u,
						includeHidden = false
					},
					token
				});
				await requestTask.WaitAsync(token).ConfigureAwait(continueOnCapturedContext: false);
				JsonElement response = (JsonElement)requestTask.GetType().GetProperty("Result").GetValue(requestTask);
				CatalogSnapshot.ReadPage(response, models);
				text3 = ((response.TryGetProperty("nextCursor", out var value) && value.ValueKind == JsonValueKind.String) ? value.GetString() : null);
				if (!string.IsNullOrWhiteSpace(text3) && !seenCursors.Add(text3))
				{
					throw new InvalidOperationException("model/list returned a repeated page cursor.");
				}
			}
			while (!string.IsNullOrWhiteSpace(text3));
			token.ThrowIfCancellationRequested();
			lock (Sync)
			{
				if (generation != _generation || !_enabled)
				{
					return;
				}
				_snapshot = CatalogSnapshot.FromModels(models);
			}
			Plugin.LogSource.LogInfo("Agent catalog refreshed from model/list: models=" + models.Count);
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
			lock (Sync)
			{
				if (generation == _generation)
				{
					_snapshot = Empty();
				}
			}
		}
		catch (Exception error)
		{
			lock (Sync)
			{
				if (generation == _generation)
				{
					_snapshot = Empty();
				}
			}
			Plugin.LogSource.LogWarning("Agent model/list refresh failed: " + Unwrap(error).Message);
		}
		finally
		{
			lock (Sync)
			{
				if (generation == _generation)
				{
					_refresh = null;
					_cancellation = null;
				}
			}
			cancellation.Dispose();
		}
	}

	private static Exception Unwrap(Exception error)
	{
		if (!(error is TargetInvocationException ex) || error.InnerException == null)
		{
			return error;
		}
		return ex.InnerException;
	}

	private static CatalogSnapshot Empty()
	{
		return CatalogSnapshot.FromModels(new Dictionary<string, CatalogModel>(StringComparer.Ordinal));
	}
}
