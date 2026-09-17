using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Just_Lilith.Core.Speech;

public sealed class TtsServiceManager : ITtsServiceManager, IDisposable
{
	private enum Health
	{
		Missing,
		Loading,
		Ready,
		Failed,
		Unknown
	}

	private sealed class ServiceFailure : Exception
	{
		public ServiceFailure(string message)
			: base(message)
		{
		}
	}

	private readonly object gate = new object();

	private readonly TtsServiceOptions options;

	private readonly Queue<string> logs = new Queue<string>();

	private TtsServiceState state = new TtsServiceState(TtsServicePhase.Off, "TTS 服务已关闭");

	private CancellationTokenSource? runCancellation;

	private ITtsManagedProcess? owned;

	private Task worker = Task.CompletedTask;

	private long generation;

	private bool enabled;

	private bool stopped;

	private string endpoint = "";

	public TtsServiceState State
	{
		get
		{
			lock (gate)
			{
				return state;
			}
		}
	}

	public IReadOnlyList<string> RecentLog
	{
		get
		{
			lock (gate)
			{
				return logs.ToArray();
			}
		}
	}

	public Task Completion
	{
		get
		{
			lock (gate)
			{
				return worker;
			}
		}
	}

	public TtsServiceManager(TtsServiceOptions options)
	{
		this.options = options ?? throw new ArgumentNullException("options");
		bool flag = options.StartupTimeout <= TimeSpan.Zero || options.StartupTimeout > TimeSpan.FromMinutes(10.0) || options.HealthPollInterval < TimeSpan.FromMilliseconds(10.0) || options.HealthPollInterval > TimeSpan.FromSeconds(30.0) || options.HealthRequestTimeout < TimeSpan.FromMilliseconds(50.0) || options.HealthRequestTimeout > TimeSpan.FromSeconds(30.0);
		if (!flag)
		{
			int logCapacity = options.LogCapacity;
			flag = ((logCapacity < 1 || logCapacity > 256) ? true : false);
		}
		if (flag)
		{
			throw new ArgumentException("Invalid TTS lifecycle limits.", "options");
		}
	}

	public void SetEnabled(bool value, string serviceUrl)
	{
		string url = "";
		bool flag = false;
		if (value)
		{
			try
			{
				url = NormalizeEndpoint(serviceUrl);
			}
			catch (ArgumentException)
			{
				flag = true;
			}
		}
		CancellationTokenSource cancellationTokenSource;
		ITtsManagedProcess ttsManagedProcess;
		Task previous;
		lock (gate)
		{
			if (stopped || (value == enabled && (!value || endpoint == url) && (!value || state.Phase != TtsServicePhase.Failed)))
			{
				return;
			}
			enabled = value;
			endpoint = url;
			long revision = ++generation;
			cancellationTokenSource = runCancellation;
			ttsManagedProcess = owned;
			runCancellation = null;
			previous = worker;
			if (value && !flag)
			{
				CancellationTokenSource cancellation = new CancellationTokenSource();
				runCancellation = cancellation;
				state = ((owned != null) ? new TtsServiceState(TtsServicePhase.Stopping, "正在等待先前 TTS 服务退出", Owned: true) : new TtsServiceState(TtsServicePhase.Starting, "TTS 服务启动中"));
				worker = Task.Run(async delegate
				{
					try
					{
						await previous.ConfigureAwait(continueOnCapturedContext: false);
					}
					catch
					{
					}
					lock (gate)
					{
						if (owned != null)
						{
							if (generation == revision)
							{
								state = new TtsServiceState(TtsServicePhase.Stopping, "先前 TTS 服务尚未退出", Owned: true);
							}
							return;
						}
					}
					if (!cancellation.IsCancellationRequested)
					{
						await RunAsync(revision, url, cancellation.Token).ConfigureAwait(continueOnCapturedContext: false);
					}
				});
			}
			else
			{
				state = ((flag && previous.IsCompleted) ? new TtsServiceState(TtsServicePhase.Failed, "TTS 服务地址须为本机 127.0.0.1 的有效端口") : new TtsServiceState((!previous.IsCompleted) ? TtsServicePhase.Stopping : TtsServicePhase.Off, previous.IsCompleted ? "TTS 服务已关闭" : "TTS 服务关闭中", owned != null));
				worker = FinishStopAsync(previous, revision, flag);
			}
		}
		ttsManagedProcess?.RequestStop();
		cancellationTokenSource?.Cancel();
		if (cancellationTokenSource != null)
		{
			DisposeCancellationAfterAsync(cancellationTokenSource, previous);
		}
	}

	private static async Task DisposeCancellationAfterAsync(CancellationTokenSource cancellation, Task previous)
	{
		try
		{
			await previous.ConfigureAwait(continueOnCapturedContext: false);
		}
		catch
		{
		}
		cancellation.Dispose();
	}

	private async Task FinishStopAsync(Task previous, long revision, bool failed)
	{
		try
		{
			await previous.ConfigureAwait(continueOnCapturedContext: false);
		}
		catch
		{
		}
		lock (gate)
		{
			if (generation == revision)
			{
				state = ((owned != null) ? new TtsServiceState(TtsServicePhase.Stopping, "TTS 服务退出尚未确认，请查看诊断日志", Owned: true) : (failed ? new TtsServiceState(TtsServicePhase.Failed, "TTS 服务地址须为本机 127.0.0.1 的有效端口") : new TtsServiceState(TtsServicePhase.Off, "TTS 服务已关闭")));
			}
		}
	}

	public void RequestStop()
	{
		ITtsManagedProcess ttsManagedProcess;
		CancellationTokenSource cancellationTokenSource;
		Task previous;
		lock (gate)
		{
			if (stopped)
			{
				return;
			}
			stopped = true;
			enabled = false;
			generation++;
			ttsManagedProcess = owned;
			cancellationTokenSource = runCancellation;
			runCancellation = null;
			state = new TtsServiceState((!worker.IsCompleted) ? TtsServicePhase.Stopping : TtsServicePhase.Off, worker.IsCompleted ? "TTS 服务已关闭" : "TTS 服务关闭中", owned != null);
			previous = worker;
			worker = FinishStopAsync(previous, generation, failed: false);
		}
		ttsManagedProcess?.RequestStop();
		cancellationTokenSource?.Cancel();
		if (cancellationTokenSource != null)
		{
			DisposeCancellationAfterAsync(cancellationTokenSource, previous);
		}
	}

	public void Dispose()
	{
		RequestStop();
	}

	private bool IsCurrent(long revision, CancellationToken cancellation)
	{
		lock (gate)
		{
			return generation == revision && enabled && !stopped && !cancellation.IsCancellationRequested;
		}
	}

	private void Publish(long revision, TtsServicePhase phase, string message, bool isOwned = false)
	{
		lock (gate)
		{
			if (generation == revision && enabled && !stopped)
			{
				state = new TtsServiceState(phase, message, isOwned);
			}
		}
	}

	private void AppendLog(string value, string instance)
	{
		value = TtsServiceManagerDiagnostics.SanitizeText(value.Replace(instance, "[instance]", StringComparison.Ordinal));
		value = new string(value.Where((char c) => !char.IsControl(c) || c == '\t').Take(512).ToArray());
		lock (gate)
		{
			while (logs.Count >= options.LogCapacity)
			{
				logs.Dequeue();
			}
			logs.Enqueue(value);
		}
	}

	private async Task RunAsync(long revision, string url, CancellationToken cancellation)
	{
		ITtsManagedProcess child = null;
		TtsServiceManagerDiagnostics diagnostics = null;
		string token = Guid.NewGuid().ToString("N");
		string failure = null;
		bool exitConfirmed = true;
		try
		{
			_ = 2;
			try
			{
				string runtime = Path.GetFullPath(options.RuntimeDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				TtsServiceManagerProcess.AssertPlainPath(runtime);
				diagnostics = new TtsServiceManagerDiagnostics(runtime);
				Publish(revision, TtsServicePhase.Starting, "TTS 服务启动中");
				Log("lifecycle: attempt_started; generation=" + revision + "; endpoint=" + url);
				string project = ProjectIdentity(runtime);
				using SocketsHttpHandler handler = new SocketsHttpHandler
				{
					UseProxy = false,
					AllowAutoRedirect = false,
					ConnectTimeout = options.HealthRequestTimeout
				};
				using HttpClient http = new HttpClient(handler)
				{
					Timeout = Timeout.InfiniteTimeSpan
				};
				Uri uri = new Uri(url);
				Stopwatch startupClock = Stopwatch.StartNew();
				Health health = await ProbeAsync(http, uri, project, null, cancellation).ConfigureAwait(continueOnCapturedContext: false);
				Log("lifecycle: initial_health=" + health);
				if (!IsCurrent(revision, cancellation))
				{
					return;
				}
				if ((uint)(health - 3) <= 1u)
				{
					throw new ServiceFailure((health == Health.Unknown) ? "端口已占用或服务身份未确认" : "TTS 服务模型加载失败");
				}
				if ((uint)(health - 1) <= 1u)
				{
					throw new ServiceFailure("检测到外部 TTS 实例；请先停止手动服务再开启，本插件未接管");
				}
				if (health != Health.Missing)
				{
					goto IL_03dd;
				}
				child = options.ProcessFactory?.Invoke(runtime, uri.Port, token, Log) ?? new TtsServiceManagerProcess(runtime, uri.Port, token, Log);
				lock (gate)
				{
					if (generation != revision || stopped || !enabled || cancellation.IsCancellationRequested)
					{
						return;
					}
					owned = child;
					goto IL_03ba;
				}
				IL_03ba:
				child.Start();
				Publish(revision, TtsServicePhase.Starting, "TTS 服务启动中", isOwned: true);
				goto IL_03dd;
				IL_03dd:
				bool ready = false;
				int missed = 0;
				while (IsCurrent(revision, cancellation))
				{
					if (child?.HasExited ?? false)
					{
						int exitCode = child.ExitCode;
						string text = exitCode.ToString();
						uint num = (uint)exitCode;
						Log("lifecycle: unexpected_exit; exit_code=" + text + "; exit_hex=0x" + num.ToString("X8"));
						string[] obj = new string[5]
						{
							"TTS 服务进程已退出（",
							exitCode.ToString(),
							" / 0x",
							null,
							null
						};
						num = (uint)exitCode;
						obj[3] = num.ToString("X8");
						obj[4] = "）";
						throw new ServiceFailure(string.Concat(obj));
					}
					Health health2 = await ProbeAsync(http, uri, project, (child == null) ? null : token, cancellation).ConfigureAwait(continueOnCapturedContext: false);
					if (!IsCurrent(revision, cancellation))
					{
						return;
					}
					if (health2 == Health.Ready)
					{
						missed = 0;
						if (!ready)
						{
							ready = true;
							Log("lifecycle: ready; owned=True");
							Publish(revision, TtsServicePhase.Ready, "TTS 服务已就绪", isOwned: true);
						}
					}
					else
					{
						if ((uint)(health2 - 3) <= 1u)
						{
							throw new ServiceFailure((health2 == Health.Unknown) ? "TTS 服务身份或端口冲突" : "TTS 服务模型加载失败");
						}
						if (ready)
						{
							int num2 = missed + 1;
							missed = num2;
							if (num2 >= 3)
							{
								throw new ServiceFailure("TTS 服务连接已中断");
							}
						}
					}
					if (!ready && startupClock.Elapsed >= options.StartupTimeout)
					{
						throw new ServiceFailure("TTS 服务启动超时");
					}
					await Task.Delay(options.HealthPollInterval, cancellation).ConfigureAwait(continueOnCapturedContext: false);
				}
				goto end_IL_009e;
			}
			catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
			{
				Log("lifecycle: stop_requested");
				goto end_IL_009e;
			}
			catch (ServiceFailure serviceFailure)
			{
				failure = serviceFailure.Message;
				Log("lifecycle: failure_detected; " + serviceFailure.Message);
				goto end_IL_009e;
			}
			catch (Exception ex2)
			{
				Log("lifecycle: managed_failure; type=" + ex2.GetType().Name + "; hresult=0x" + ((uint)ex2.HResult).ToString("X8") + ((ex2 is Win32Exception ex3) ? ("; win32=" + ex3.NativeErrorCode) : ""));
				failure = "TTS 服务启动或运行失败，请检查本地运行环境";
				goto end_IL_009e;
			}
			end_IL_009e:;
		}
		finally
		{
			if (failure != null && child != null)
			{
				Publish(revision, TtsServicePhase.Stopping, "TTS 服务失败，正在完成进程清理", isOwned: true);
			}
			if (child != null)
			{
				child.RequestStop();
				try
				{
					await child.WaitForExitAsync(TimeSpan.FromSeconds(5.0)).ConfigureAwait(continueOnCapturedContext: false);
					_ = child.HasExited;
					Stopwatch startupClock = Stopwatch.StartNew();
					int missed = new Uri(url).Port;
					while (HasLocalListener(missed) && startupClock.Elapsed < TimeSpan.FromSeconds(1.0))
					{
						await Task.Delay(20).ConfigureAwait(continueOnCapturedContext: false);
					}
				}
				catch (Exception ex4)
				{
					Log("lifecycle: cleanup_error=" + ex4.GetType().Name);
				}
				while (!child.HasExited)
				{
					Publish(revision, TtsServicePhase.Stopping, "TTS 服务退出尚未确认，请查看诊断日志", isOwned: true);
					await Task.Delay(100).ConfigureAwait(continueOnCapturedContext: false);
				}
				exitConfirmed = true;
				bool ready = false;
				try
				{
					await child.DrainLogsAsync(TimeSpan.FromSeconds(3.0)).ConfigureAwait(continueOnCapturedContext: false);
					ready = true;
				}
				catch (Exception ex5)
				{
					Log("lifecycle: drain_error=" + ex5.GetType().Name);
				}
				int exitCode2 = child.ExitCode;
				string[] obj2 = new string[8]
				{
					"lifecycle: child_retired; exit_confirmed=",
					exitConfirmed.ToString(),
					"; exit_code=",
					exitCode2.ToString(),
					"; exit_hex=0x",
					null,
					null,
					null
				};
				uint num = (uint)exitCode2;
				obj2[5] = num.ToString("X8");
				obj2[6] = "; pipe_drain_completed=";
				obj2[7] = ready.ToString();
				Log(string.Concat(obj2));
				if (exitConfirmed)
				{
					child.Dispose();
					lock (gate)
					{
						if (owned == child)
						{
							owned = null;
						}
					}
				}
				else
				{
					failure = "TTS 服务退出尚未确认";
				}
			}
			Log("lifecycle: attempt_finished; failed=" + (failure != null) + "; exit_confirmed=" + exitConfirmed);
			bool flag = diagnostics?.WriteFailed ?? true;
			diagnostics?.Dispose();
			if (failure != null)
			{
				Publish(revision, exitConfirmed ? TtsServicePhase.Failed : TtsServicePhase.Stopping, failure + "；" + (flag ? "诊断日志写入失败，请检查 tts/logs 目录" : "详情见 tts/logs/managed-service.log"), !exitConfirmed);
			}
		}
		void Log(string text2)
		{
			text2 = TtsServiceManagerDiagnostics.SanitizeText(text2.Replace(token, "[instance]", StringComparison.Ordinal));
			AppendLog(text2, token);
			diagnostics?.Write(text2, child?.ProcessId);
		}
	}

	private async Task<Health> ProbeAsync(HttpClient http, Uri endpointUri, string project, string? instance, CancellationToken cancellation)
	{
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
		timeout.CancelAfter(options.HealthRequestTimeout);
		try
		{
			if (!HasLocalListener(endpointUri.Port))
			{
				return Health.Missing;
			}
			using HttpResponseMessage response = await http.GetAsync(new Uri(endpointUri, "/health"), HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(continueOnCapturedContext: false);
			if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength > 8192)
			{
				return Health.Unknown;
			}
			using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(continueOnCapturedContext: false);
			byte[] bytes = new byte[8193];
			int count = 0;
			int read = default(int);
			while (true)
			{
				bool readMore = count < bytes.Length;
				if (readMore)
				{
					int num;
					read = (num = await stream.ReadAsync(bytes.AsMemory(count), timeout.Token).ConfigureAwait(continueOnCapturedContext: false));
					readMore = num > 0;
				}
				if (!readMore)
				{
					break;
				}
				count += read;
			}
			if (count > 8192)
			{
				return Health.Unknown;
			}
			using JsonDocument jsonDocument = JsonDocument.Parse(bytes.AsMemory(0, count));
			JsonElement rootElement = jsonDocument.RootElement;
			if (Text(rootElement, "service") != "Just_Lilith.TTS" || Text(rootElement, "project_id") != project || Text(rootElement, "model_version") != "v4" || !Flag(rootElement, "gpu_only") || (instance != null && Text(rootElement, "instance_token") != instance))
			{
				return Health.Unknown;
			}
			bool flag;
			switch (Text(rootElement, "status"))
			{
			case "error":
				return Health.Failed;
			case "ready":
				return (Text(rootElement, "device") == "cuda" && Flag(rootElement, "model_loaded")) ? Health.Ready : Health.Failed;
			case "starting":
			case "loading":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			return flag ? Health.Loading : Health.Unknown;
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex2) when (((ex2 is HttpRequestException || ex2 is IOException || ex2 is JsonException || ex2 is OperationCanceledException || ex2 is SocketException) ? 1 : 0) != 0)
		{
			return HasLocalListener(endpointUri.Port) ? Health.Unknown : Health.Missing;
		}
	}

	private static bool HasLocalListener(int port)
	{
		return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any((IPEndPoint listener) => listener.Port == port && (listener.Address.Equals(IPAddress.Loopback) || listener.Address.Equals(IPAddress.Any) || listener.Address.Equals(IPAddress.IPv6Any)));
	}

	private static string Text(JsonElement root, string name)
	{
		if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return "";
		}
		return value.GetString() ?? "";
	}

	private static bool Flag(JsonElement root, string name)
	{
		if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value))
		{
			return value.ValueKind == JsonValueKind.True;
		}
		return false;
	}

	internal static string ProjectIdentity(string runtime)
	{
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(runtime).TrimEnd('\\', '/').Replace('\\', '/')
			.ToLowerInvariant()))).ToLowerInvariant();
	}

	private static string NormalizeEndpoint(string value)
	{
		Uri result = default(Uri);
		bool flag = string.IsNullOrWhiteSpace(value) || value.Length > 2048 || value.Any(char.IsControl) || value.Contains('\\') || !Uri.TryCreate(value, UriKind.Absolute, out result) || result.Scheme != "http" || result.UserInfo.Length > 0 || result.Query.Length > 0 || result.Fragment.Length > 0 || result.AbsolutePath != "/";
		if (!flag)
		{
			int port = result.Port;
			bool flag2 = ((port < 1024 || port > 65535) ? true : false);
			flag = flag2;
		}
		if (flag || (result.Host != "127.0.0.1" && !result.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)))
		{
			throw new ArgumentException("Expected the local IPv4 TTS endpoint.");
		}
		return "http://127.0.0.1:" + result.Port;
	}
}
