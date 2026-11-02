using System;

namespace Pigless.CameraMod;

internal readonly struct TabletRect
{
	internal readonly float X;

	internal readonly float Y;

	internal readonly float Width;

	internal readonly float Height;

	internal TabletRect(float x, float y, float width, float height)
	{
		X = x;
		Y = y;
		Width = width;
		Height = height;
	}

	internal bool Contains(float x, float y)
	{
		if (Finite(x) && Finite(y) && Finite(X) && Finite(Y) && Finite(Width) && Finite(Height) && Width > 0f && Height > 0f && Math.Abs(x - X) <= Width * 0.5f)
		{
			return Math.Abs(y - Y) <= Height * 0.5f;
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
