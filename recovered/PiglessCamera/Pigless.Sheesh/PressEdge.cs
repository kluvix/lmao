namespace Pigless.Sheesh;

internal sealed class PressEdge
{
	private bool armed;

	internal bool Poll(bool available, bool pressed)
	{
		if (!available)
		{
			Reset();
			return false;
		}
		if (!pressed)
		{
			armed = true;
			return false;
		}
		if (!armed)
		{
			return false;
		}
		armed = false;
		return true;
	}

	internal void Reset()
	{
		armed = false;
	}
}
