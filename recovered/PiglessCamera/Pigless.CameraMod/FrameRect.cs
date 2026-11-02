namespace Pigless.CameraMod;

internal readonly struct FrameRect
{
	internal readonly float X;

	internal readonly float Y;

	internal readonly float Width;

	internal readonly float Height;

	internal FrameRect(float x, float y, float width, float height)
	{
		X = x;
		Y = y;
		Width = width;
		Height = height;
	}
}
