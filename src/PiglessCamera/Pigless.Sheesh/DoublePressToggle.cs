using System;

namespace Pigless.Sheesh;

internal sealed class DoublePressToggle
{
	private readonly float window;

	private readonly PressEdge edge = new PressEdge();

	private bool awaitingSecond;

	private float firstPressAt;

	private float lastPollAt = float.NaN;

	internal DoublePressToggle(float window = 0.4f)
	{
		if (float.IsNaN(window) || float.IsInfinity(window) || window <= 0f)
		{
			throw new ArgumentOutOfRangeException("window", "The double-press window must be finite and positive.");
		}
		this.window = window;
	}

	internal bool Poll(bool available, bool pressed, float now)
	{
		if (!available || float.IsNaN(now) || float.IsInfinity(now))
		{
			Reset();
			return false;
		}
		if (!float.IsNaN(lastPollAt) && now < lastPollAt)
		{
			Reset();
		}
		lastPollAt = now;
		if (awaitingSecond && now - firstPressAt > window)
		{
			awaitingSecond = false;
		}
		if (!edge.Poll(available: true, pressed))
		{
			return false;
		}
		if (awaitingSecond)
		{
			awaitingSecond = false;
			return true;
		}
		firstPressAt = now;
		awaitingSecond = true;
		return false;
	}

	internal void Reset()
	{
		awaitingSecond = false;
		firstPressAt = 0f;
		lastPollAt = float.NaN;
		edge.Reset();
	}
}
