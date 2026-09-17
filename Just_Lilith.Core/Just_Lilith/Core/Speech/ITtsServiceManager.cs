namespace Just_Lilith.Core.Speech;

public interface ITtsServiceManager
{
	TtsServiceState State { get; }

	void SetEnabled(bool enabled, string serviceUrl);

	void RequestStop();
}
