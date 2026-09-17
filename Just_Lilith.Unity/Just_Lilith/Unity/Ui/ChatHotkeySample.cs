namespace Just_Lilith.Unity.Ui;

internal readonly struct ChatHotkeySample(bool edgeObserved, bool isDown, string source)
{
	public bool EdgeObserved { get; } = edgeObserved;

	public bool IsDown { get; } = isDown;

	public string Source { get; } = source;
}
