using System;
using System.Threading;
using System.Threading.Tasks;

namespace Just_Lilith.Core.Threading;

public interface IMainThreadDispatcher
{
	bool IsMainThread { get; }

	Task Post(Action action, CancellationToken cancellationToken = default(CancellationToken));
}
