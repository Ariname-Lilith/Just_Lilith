using System;

namespace Just_Lilith.Core.Llm;

public static class BubbleDeliveryRetryPolicy
{
	public static TimeSpan RetryInterval { get; } = TimeSpan.FromMilliseconds(250.0);

	public static TimeSpan MaximumWait { get; } = TimeSpan.FromSeconds(45.0);

	public static bool HasExpired(DateTimeOffset enqueuedAt, DateTimeOffset now)
	{
		if (now < enqueuedAt)
		{
			return false;
		}
		return now - enqueuedAt >= MaximumWait;
	}
}
