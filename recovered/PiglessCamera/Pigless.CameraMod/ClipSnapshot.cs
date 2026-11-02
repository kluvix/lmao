using System.Collections.Generic;

namespace Pigless.CameraMod;

internal sealed class ClipSnapshot
{
	internal readonly int Width;

	internal readonly int Height;

	internal readonly int FramesPerSecond;

	internal readonly IReadOnlyList<byte[]> Frames;

	internal double Seconds => (double)Frames.Count / (double)FramesPerSecond;

	internal ClipSnapshot(int width, int height, int fps, byte[][] frames)
	{
		Width = width;
		Height = height;
		FramesPerSecond = fps;
		Frames = frames;
	}
}
