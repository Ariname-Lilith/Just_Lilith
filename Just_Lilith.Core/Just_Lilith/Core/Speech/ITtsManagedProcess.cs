using System;
using System.Threading.Tasks;

namespace Just_Lilith.Core.Speech;

internal interface ITtsManagedProcess : IDisposable
{
	bool HasExited { get; }

	int ExitCode { get; }

	int? ProcessId { get; }

	void Start();

	void RequestStop();

	Task WaitForExitAsync(TimeSpan timeout);

	Task DrainLogsAsync(TimeSpan timeout)
	{
		return Task.CompletedTask;
	}
}
