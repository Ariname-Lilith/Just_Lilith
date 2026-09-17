namespace Just_Lilith.Core.Speech;

public sealed record PcmWaveData(int SampleRate, int Channels, float[] Samples)
{
	public int SampleFrames => Samples.Length / Channels;

	public double DurationSeconds => (double)SampleFrames / (double)SampleRate;
}
