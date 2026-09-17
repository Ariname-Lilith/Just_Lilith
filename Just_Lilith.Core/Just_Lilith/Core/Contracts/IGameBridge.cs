namespace Just_Lilith.Core.Contracts;

public interface IGameBridge
{
	PoseSnapshot CapturePose();

	void ShowReply(DisplayReply reply);

	void PlayAudio(SpeechAudio audio);

	void StopAudio();
}
