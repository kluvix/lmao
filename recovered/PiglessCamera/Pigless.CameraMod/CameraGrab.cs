namespace Pigless.CameraMod;

internal static class CameraGrab
{
	internal static GrabHand Select(GrabHand current, bool leftHeld, bool rightHeld, float leftDistance, float rightDistance, float reach = 0.22f)
	{
		if (current == GrabHand.Left && leftHeld)
		{
			return GrabHand.Left;
		}
		if (current == GrabHand.Right && rightHeld)
		{
			return GrabHand.Right;
		}
		if (!Finite(reach) || reach < 0f)
		{
			return GrabHand.None;
		}
		bool flag = leftHeld && Eligible(leftDistance, reach);
		bool flag2 = rightHeld && Eligible(rightDistance, reach);
		if (flag && flag2)
		{
			if (!(leftDistance <= rightDistance))
			{
				return GrabHand.Right;
			}
			return GrabHand.Left;
		}
		if (flag)
		{
			return GrabHand.Left;
		}
		if (flag2)
		{
			return GrabHand.Right;
		}
		return GrabHand.None;
	}

	private static bool Eligible(float distance, float reach)
	{
		if (Finite(distance) && distance >= 0f)
		{
			return distance <= reach;
		}
		return false;
	}

	private static bool Finite(float value)
	{
		if (!float.IsNaN(value))
		{
			return !float.IsInfinity(value);
		}
		return false;
	}
}
