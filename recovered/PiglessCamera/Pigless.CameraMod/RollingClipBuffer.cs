using System;
using System.Collections.Generic;

namespace Pigless.CameraMod;

internal sealed class RollingClipBuffer
{
	private readonly struct Frame
	{
		internal readonly double Time;

		internal readonly byte[] Jpeg;

		internal Frame(double time, byte[] jpeg)
		{
			Time = time;
			Jpeg = jpeg;
		}
	}

	private readonly List<Frame> frames = new List<Frame>();

	private readonly int width;

	private readonly int height;

	private readonly int fps;

	private readonly int maxFrames;

	private readonly int maxBytes;

	private readonly double seconds;

	private long bytes;

	internal int Count => frames.Count;

	internal long Bytes => bytes;

	internal double BufferedSeconds => (double)OutputCount() / (double)fps;

	internal RollingClipBuffer(int width, int height, int fps = 15, double seconds = 15.0, int maxBytes = 33554432)
	{
		if (width < 1 || width > 4096 || height < 1 || height > 4096)
		{
			throw new ArgumentOutOfRangeException("width");
		}
		if (fps < 1 || fps > 60)
		{
			throw new ArgumentOutOfRangeException("fps");
		}
		if (!Finite(seconds) || seconds * (double)fps < 1.0 || seconds * (double)fps > 900.0)
		{
			throw new ArgumentOutOfRangeException("seconds");
		}
		if (maxBytes < 1)
		{
			throw new ArgumentOutOfRangeException("maxBytes");
		}
		this.width = width;
		this.height = height;
		this.fps = fps;
		this.seconds = seconds;
		this.maxBytes = maxBytes;
		maxFrames = Math.Max(1, (int)Math.Ceiling(seconds * (double)fps));
	}

	internal bool Add(double time, byte[] jpeg)
	{
		if (!Finite(time) || time < 0.0 || jpeg == null || jpeg.Length == 0 || jpeg.Length > maxBytes)
		{
			return false;
		}
		if (frames.Count > 0)
		{
			double time2 = frames[frames.Count - 1].Time;
			if (time < time2 || time - time2 > 2.0)
			{
				Clear();
			}
			else if (time == time2)
			{
				return false;
			}
		}
		frames.Add(new Frame(time, jpeg));
		bytes += jpeg.Length;
		double num = time - seconds;
		while (frames.Count > 1 && frames[1].Time <= num)
		{
			RemoveFirst();
		}
		while (frames.Count > maxFrames + 2 || bytes > maxBytes)
		{
			RemoveFirst();
		}
		return true;
	}

	internal ClipSnapshot Snapshot()
	{
		int num = OutputCount();
		if (num == 0)
		{
			return null;
		}
		double time = frames[frames.Count - 1].Time;
		double num2 = Math.Max(frames[0].Time, time - (double)(num - 1) / (double)fps);
		byte[][] array = new byte[num][];
		int i = 0;
		for (int j = 0; j < num; j++)
		{
			for (double num3 = num2 + (double)j / (double)fps; i + 1 < frames.Count && frames[i + 1].Time <= num3 + 1E-06; i++)
			{
			}
			array[j] = frames[i].Jpeg;
		}
		return new ClipSnapshot(width, height, fps, array);
	}

	private int OutputCount()
	{
		if (frames.Count == 0)
		{
			return 0;
		}
		double val = Math.Max(0.0, frames[frames.Count - 1].Time - frames[0].Time);
		return Math.Max(1, Math.Min(maxFrames, (int)Math.Floor(Math.Min(seconds, val) * (double)fps + 1E-06) + 1));
	}

	private void RemoveFirst()
	{
		bytes -= frames[0].Jpeg.Length;
		frames.RemoveAt(0);
	}

	internal void Clear()
	{
		frames.Clear();
		bytes = 0L;
	}

	private static bool Finite(double value)
	{
		if (!double.IsNaN(value))
		{
			return !double.IsInfinity(value);
		}
		return false;
	}
}
