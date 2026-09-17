namespace Just_Lilith.Unity.Ui;

internal sealed class F7PressDeduplicator
{
	private const int QuietSamplesToRearm = 2;

	private const float CrossSourceWindowSeconds = 0.12f;

	private bool _latched;

	private int _quietSamples;

	private float _rearmNotBefore;

	public bool Consume(bool edgeObserved, bool isDown, float now)
	{
		if (!_latched)
		{
			if (!edgeObserved)
			{
				return false;
			}
			_latched = true;
			_quietSamples = 0;
			_rearmNotBefore = now + 0.12f;
			return true;
		}
		if (edgeObserved | isDown)
		{
			_quietSamples = 0;
			return false;
		}
		if (_quietSamples < 2)
		{
			_quietSamples++;
		}
		if (_quietSamples >= 2 && now >= _rearmNotBefore)
		{
			_latched = false;
			_quietSamples = 0;
		}
		return false;
	}
}
