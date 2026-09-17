using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Just_Lilith.Core.Speech;

internal sealed class TtsServiceManagerProcess : ITtsManagedProcess, IDisposable
{
	private static class Native
	{
		internal struct SecurityAttributes
		{
			internal int Length;

			internal IntPtr SecurityDescriptor;

			[MarshalAs(UnmanagedType.Bool)]
			internal bool InheritHandle;
		}

		internal struct StartupInfo
		{
			internal int Size;

			internal IntPtr Reserved;

			internal IntPtr Desktop;

			internal IntPtr Title;

			internal uint X;

			internal uint Y;

			internal uint XSize;

			internal uint YSize;

			internal uint XCountChars;

			internal uint YCountChars;

			internal uint FillAttribute;

			internal uint Flags;

			internal ushort ShowWindow;

			internal ushort Reserved2Size;

			internal IntPtr Reserved2;

			internal IntPtr StandardInput;

			internal IntPtr StandardOutput;

			internal IntPtr StandardError;
		}

		internal struct StartupInfoEx
		{
			internal StartupInfo StartupInfo;

			internal IntPtr AttributeList;
		}

		internal struct ProcessInformation
		{
			internal IntPtr Process;

			internal IntPtr Thread;

			internal uint ProcessId;

			internal uint ThreadId;
		}

		internal struct JobBasicLimitInformation
		{
			internal long PerProcessUserTimeLimit;

			internal long PerJobUserTimeLimit;

			internal uint LimitFlags;

			internal UIntPtr MinimumWorkingSetSize;

			internal UIntPtr MaximumWorkingSetSize;

			internal uint ActiveProcessLimit;

			internal UIntPtr Affinity;

			internal uint PriorityClass;

			internal uint SchedulingClass;
		}

		internal struct IoCounters
		{
			internal ulong ReadOperationCount;

			internal ulong WriteOperationCount;

			internal ulong OtherOperationCount;

			internal ulong ReadTransferCount;

			internal ulong WriteTransferCount;

			internal ulong OtherTransferCount;
		}

		internal struct JobExtendedLimitInformation
		{
			internal JobBasicLimitInformation BasicLimitInformation;

			internal IoCounters IoInfo;

			internal UIntPtr ProcessMemoryLimit;

			internal UIntPtr JobMemoryLimit;

			internal UIntPtr PeakProcessMemoryUsed;

			internal UIntPtr PeakJobMemoryUsed;
		}

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		internal static extern uint GetDllDirectory(uint length, StringBuilder path);

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref SecurityAttributes attributes, uint size);

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		internal static extern SafeFileHandle CreateFile(string name, uint access, uint share, ref SecurityAttributes attributes, uint creation, uint flags, IntPtr template);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		internal static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, ref JobExtendedLimitInformation limits, uint size);

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returnSize);

		[DllImport("kernel32.dll")]
		internal static extern void DeleteProcThreadAttributeList(IntPtr list);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, IntPtr environment, string workingDirectory, ref StartupInfoEx startup, out ProcessInformation process);

		[DllImport("kernel32.dll", SetLastError = true)]
		internal static extern uint ResumeThread(SafeFileHandle thread);

		[DllImport("kernel32.dll", SetLastError = true)]
		internal static extern uint WaitForSingleObject(SafeFileHandle handle, uint milliseconds);

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool GetExitCodeProcess(SafeFileHandle process, out uint code);
	}

	private readonly object gate = new object();

	private readonly string runtime;

	private readonly string token;

	private readonly int port;

	private readonly Action<string> log;

	private readonly string? fixturePython;

	private SafeFileHandle? job;

	private SafeFileHandle? process;

	private StreamReader? stdout;

	private StreamReader? stderr;

	private bool stopped;

	private bool disposed;

	private bool started;

	private int? pid;

	private int? finalExitCode;

	private bool exitSignaled;

	private Task stdoutPump = Task.CompletedTask;

	private Task stderrPump = Task.CompletedTask;

	public int? ProcessId
	{
		get
		{
			lock (gate)
			{
				return pid;
			}
		}
	}

	public bool HasExited
	{
		get
		{
			lock (gate)
			{
				if (process == null)
				{
					return !started || exitSignaled;
				}
				if (Native.WaitForSingleObject(process, 0u) == 0)
				{
					exitSignaled = true;
					if (Native.GetExitCodeProcess(process, out var code))
					{
						finalExitCode = (int)code;
					}
					return true;
				}
				return false;
			}
		}
	}

	public int ExitCode
	{
		get
		{
			lock (gate)
			{
				if (process != null && Native.GetExitCodeProcess(process, out var code))
				{
					if (code != 259 || exitSignaled)
					{
						finalExitCode = (int)code;
					}
					return (int)code;
				}
				return finalExitCode ?? (-1);
			}
		}
	}

	internal TtsServiceManagerProcess(string runtime, int port, string token, Action<string> log, string? fixturePython = null)
	{
		this.runtime = runtime;
		this.port = port;
		this.token = token;
		this.log = log;
		this.fixturePython = fixturePython;
	}

	internal static void AssertPlainPath(string path)
	{
		for (string text = Path.GetFullPath(path); text != null; text = Path.GetDirectoryName(text))
		{
			if ((File.Exists(text) || Directory.Exists(text)) && (File.GetAttributes(text) & FileAttributes.ReparsePoint) != 0)
			{
				throw new IOException("Reparse points are not valid TTS lifecycle paths.");
			}
		}
	}

	public void Start()
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("TTS lifecycle requires Windows.");
		}
		string text = fixturePython ?? Path.Combine(runtime, "runtime", "python.exe");
		string text2 = Path.Combine(runtime, "service.py");
		AssertPlainPath(text);
		AssertPlainPath(text2);
		if (!File.Exists(text) || !File.Exists(text2))
		{
			throw new FileNotFoundException("Project-local TTS entry point is missing.");
		}
		log("lifecycle: parent_pid=" + Environment.ProcessId + "; parent_image=" + Environment.ProcessPath);
		log("lifecycle: parent_cwd=" + Environment.CurrentDirectory + "; parent_dll_directory=" + ParentDllDirectory());
		string[] source = new string[6] { "DOORSTOP_INITIALIZED", "DOORSTOP_INVOKE_DLL_PATH", "DOORSTOP_MONO_LIB_PATH", "DOTNET_ROOT", "DOTNET_STARTUP_HOOKS", "__COMPAT_LAYER" };
		log("lifecycle: loader_environment_names_present=" + string.Join(",", source.Where((string name) => Environment.GetEnvironmentVariable(name) != null)));
		log("lifecycle: create_process; executable=runtime/python.exe; entry=service.py; port=" + port + "; device=cuda; job=atomic");
		Native.SecurityAttributes attributes = new Native.SecurityAttributes
		{
			Length = Marshal.SizeOf<Native.SecurityAttributes>(),
			InheritHandle = true
		};
		SafeFileHandle read = null;
		SafeFileHandle write = null;
		SafeFileHandle read2 = null;
		SafeFileHandle write2 = null;
		SafeFileHandle safeFileHandle = null;
		SafeFileHandle safeFileHandle2 = null;
		IntPtr intPtr = IntPtr.Zero;
		IntPtr intPtr2 = IntPtr.Zero;
		IntPtr intPtr3 = IntPtr.Zero;
		IntPtr intPtr4 = IntPtr.Zero;
		bool flag = false;
		try
		{
			Check(Native.CreatePipe(out read, out write, ref attributes, 0u));
			Check(Native.CreatePipe(out read2, out write2, ref attributes, 0u));
			Check(Native.SetHandleInformation(read, 1u, 0u));
			Check(Native.SetHandleInformation(read2, 1u, 0u));
			safeFileHandle = Native.CreateFile("NUL", 2147483648u, 3u, ref attributes, 3u, 0u, IntPtr.Zero);
			if (safeFileHandle.IsInvalid)
			{
				ThrowNative();
			}
			safeFileHandle2 = Native.CreateJobObject(IntPtr.Zero, null);
			if (safeFileHandle2.IsInvalid)
			{
				ThrowNative();
			}
			Native.JobExtendedLimitInformation limits = new Native.JobExtendedLimitInformation
			{
				BasicLimitInformation = 
				{
					LimitFlags = 8192u
				}
			};
			Check(Native.SetInformationJobObject(safeFileHandle2, 9, ref limits, (uint)Marshal.SizeOf<Native.JobExtendedLimitInformation>()));
			IntPtr size = IntPtr.Zero;
			Native.InitializeProcThreadAttributeList(IntPtr.Zero, 2, 0, ref size);
			if (size == IntPtr.Zero)
			{
				ThrowNative();
			}
			intPtr = Marshal.AllocHGlobal(size);
			Check(Native.InitializeProcThreadAttributeList(intPtr, 2, 0, ref size));
			flag = true;
			intPtr2 = Marshal.AllocHGlobal(3 * IntPtr.Size);
			Marshal.WriteIntPtr(intPtr2, 0, safeFileHandle.DangerousGetHandle());
			Marshal.WriteIntPtr(intPtr2, IntPtr.Size, write.DangerousGetHandle());
			Marshal.WriteIntPtr(intPtr2, 2 * IntPtr.Size, write2.DangerousGetHandle());
			Check(Native.UpdateProcThreadAttribute(intPtr, 0u, new IntPtr(131074), intPtr2, new IntPtr(3 * IntPtr.Size), IntPtr.Zero, IntPtr.Zero));
			intPtr3 = Marshal.AllocHGlobal(IntPtr.Size);
			Marshal.WriteIntPtr(intPtr3, safeFileHandle2.DangerousGetHandle());
			Check(Native.UpdateProcThreadAttribute(intPtr, 0u, new IntPtr(131085), intPtr3, new IntPtr(IntPtr.Size), IntPtr.Zero, IntPtr.Zero));
			Native.StartupInfoEx startup = new Native.StartupInfoEx
			{
				AttributeList = intPtr
			};
			startup.StartupInfo.Size = Marshal.SizeOf<Native.StartupInfoEx>();
			startup.StartupInfo.Flags = 256u;
			startup.StartupInfo.StandardInput = safeFileHandle.DangerousGetHandle();
			startup.StartupInfo.StandardOutput = write.DangerousGetHandle();
			startup.StartupInfo.StandardError = write2.DangerousGetHandle();
			intPtr4 = Marshal.StringToHGlobalUni(BuildEnvironment());
			StringBuilder command = new StringBuilder("\"" + text + "\" -B \"" + text2 + "\" --port " + port + " --device cuda");
			lock (gate)
			{
				if (stopped || disposed)
				{
					throw new OperationCanceledException("TTS child was stopped before creation.");
				}
				if (started)
				{
					throw new InvalidOperationException("TTS child already started.");
				}
				Check(Native.CreateProcess(text, command, IntPtr.Zero, IntPtr.Zero, inheritHandles: true, 134743044u, intPtr4, runtime, ref startup, out var processInformation));
				job = safeFileHandle2;
				safeFileHandle2 = null;
				process = new SafeFileHandle(processInformation.Process, ownsHandle: true);
				using SafeFileHandle thread = new SafeFileHandle(processInformation.Thread, ownsHandle: true);
				pid = (int)processInformation.ProcessId;
				started = true;
				if (Native.ResumeThread(thread) == uint.MaxValue)
				{
					int lastWin32Error = Marshal.GetLastWin32Error();
					job.Dispose();
					job = null;
					throw new Win32Exception(lastWin32Error);
				}
				stdout = new StreamReader(new FileStream(read, FileAccess.Read, 4096, isAsync: false), Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 1024);
				read = null;
				stderr = new StreamReader(new FileStream(read2, FileAccess.Read, 4096, isAsync: false), Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 1024);
				read2 = null;
			}
			write.Dispose();
			write = null;
			write2.Dispose();
			write2 = null;
			safeFileHandle.Dispose();
			safeFileHandle = null;
			stdoutPump = PumpAsync(stdout, "stdout");
			stderrPump = PumpAsync(stderr, "stderr");
			Action<string> action = log;
			int? num = pid;
			action("lifecycle: process_started; pid=" + num);
		}
		finally
		{
			read?.Dispose();
			write?.Dispose();
			read2?.Dispose();
			write2?.Dispose();
			safeFileHandle?.Dispose();
			safeFileHandle2?.Dispose();
			if (flag)
			{
				Native.DeleteProcThreadAttributeList(intPtr);
			}
			if (intPtr != IntPtr.Zero)
			{
				Marshal.FreeHGlobal(intPtr);
			}
			if (intPtr2 != IntPtr.Zero)
			{
				Marshal.FreeHGlobal(intPtr2);
			}
			if (intPtr3 != IntPtr.Zero)
			{
				Marshal.FreeHGlobal(intPtr3);
			}
			if (intPtr4 != IntPtr.Zero)
			{
				Marshal.FreeHGlobal(intPtr4);
			}
		}
	}

	private string BuildEnvironment()
	{
		SortedDictionary<string, string> sortedDictionary = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (DictionaryEntry environmentVariable in Environment.GetEnvironmentVariables())
		{
			if (environmentVariable.Key is string key && environmentVariable.Value is string value)
			{
				sortedDictionary[key] = value;
			}
		}
		sortedDictionary.Remove("PYTHONHOME");
		sortedDictionary.Remove("PYTHONPATH");
		sortedDictionary["PYTHONDONTWRITEBYTECODE"] = "1";
		sortedDictionary["PYTHONIOENCODING"] = "utf-8";
		sortedDictionary["PYTHONUNBUFFERED"] = "1";
		sortedDictionary["SteamNoOverlayUI"] = "1";
		sortedDictionary["JUST_LILITH_TTS_INSTANCE"] = token;
		return string.Concat(string.Concat(sortedDictionary.Select((KeyValuePair<string, string> pair) => pair.Key + "=" + pair.Value + "\0")), "\0");
	}

	private async Task PumpAsync(StreamReader reader, string source)
	{
		char[] buffer = new char[512];
		StringBuilder line = new StringBuilder(2048);
		bool oversized = false;
		try
		{
			int num;
			while ((num = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(continueOnCapturedContext: false)) > 0)
			{
				for (int i = 0; i < num; i++)
				{
					char c = buffer[i];
					if ((c == '\n' || c == '\r') ? true : false)
					{
						Emit();
					}
					else if (!oversized)
					{
						if (line.Length < 2048)
						{
							line.Append(buffer[i]);
							continue;
						}
						line.Clear();
						oversized = true;
					}
				}
			}
		}
		catch (Exception ex) when (((ex is IOException || ex is ObjectDisposedException || ex is OperationCanceledException) ? 1 : 0) != 0)
		{
			log("lifecycle: " + source + "_read_end=" + ex.GetType().Name);
		}
		finally
		{
			Emit();
		}
		void Emit()
		{
			if (oversized)
			{
				log(source + ": [oversized diagnostic line omitted]");
			}
			else if (line.Length > 0)
			{
				log(source + ": " + line.ToString().Replace(token, "[instance]", StringComparison.Ordinal));
			}
			line.Clear();
			oversized = false;
		}
	}

	public async Task DrainLogsAsync(TimeSpan timeout)
	{
		Task all = Task.WhenAll(stdoutPump, stderrPump);
		try
		{
			await all.WaitAsync(timeout).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (TimeoutException)
		{
			log("lifecycle: pipe_drain_timeout");
			stdout?.Dispose();
			stderr?.Dispose();
			await all.WaitAsync(TimeSpan.FromSeconds(1.0)).ConfigureAwait(continueOnCapturedContext: false);
			throw;
		}
	}

	private static string ParentDllDirectory()
	{
		StringBuilder stringBuilder = new StringBuilder(32768);
		uint dllDirectory = Native.GetDllDirectory((uint)stringBuilder.Capacity, stringBuilder);
		if (dllDirectory != 0)
		{
			if (dllDirectory >= stringBuilder.Capacity)
			{
				return "(too_long)";
			}
			return stringBuilder.ToString();
		}
		return "(default)";
	}

	public void RequestStop()
	{
		lock (gate)
		{
			stopped = true;
			job?.Dispose();
			job = null;
		}
	}

	public async Task WaitForExitAsync(TimeSpan timeout)
	{
		Stopwatch watch = Stopwatch.StartNew();
		while (!HasExited && watch.Elapsed < timeout)
		{
			await Task.Delay(20).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (!HasExited)
		{
			throw new TimeoutException("Owned child exit timed out.");
		}
	}

	public void Dispose()
	{
		RequestStop();
		lock (gate)
		{
			if (!disposed)
			{
				disposed = true;
				stdout?.Dispose();
				stderr?.Dispose();
				if (process != null && Native.GetExitCodeProcess(process, out var code) && (code != 259 || exitSignaled))
				{
					finalExitCode = (int)code;
				}
				process?.Dispose();
				process = null;
			}
		}
	}

	private static void Check(bool value)
	{
		if (!value)
		{
			ThrowNative();
		}
	}

	private static void ThrowNative()
	{
		throw new Win32Exception(Marshal.GetLastWin32Error());
	}
}
