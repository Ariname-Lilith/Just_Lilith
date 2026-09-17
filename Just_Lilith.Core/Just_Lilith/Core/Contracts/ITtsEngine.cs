using System.Threading;
using System.Threading.Tasks;

namespace Just_Lilith.Core.Contracts;

public interface ITtsEngine
{
	Task<SpeechAudio> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken);
}
