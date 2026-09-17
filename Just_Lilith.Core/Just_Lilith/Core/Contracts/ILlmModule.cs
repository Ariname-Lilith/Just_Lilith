using System.Collections.Generic;
using System.Threading;

namespace Just_Lilith.Core.Contracts;

public interface ILlmModule
{
	IAsyncEnumerable<ReplyEvent> GenerateAsync(ChatRequest request, CancellationToken cancellationToken);
}
