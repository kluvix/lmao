using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Pigless.CameraMod;

internal static class AviWriter
{
	internal const long MaximumFileBytes = 268435456L;

	internal const int MaximumFrames = 900;

	internal static void Write(Stream destination, int width, int height, int fps, IReadOnlyList<byte[]> jpegFrames)
	{
		if (destination == null)
		{
			throw new ArgumentNullException("destination");
		}
		if (!destination.CanWrite || !destination.CanSeek)
		{
			throw new ArgumentException("AVI output must be writable and seekable.", "destination");
		}
		if (width < 1 || width > 4096)
		{
			throw new ArgumentOutOfRangeException("width");
		}
		if (height < 1 || height > 4096)
		{
			throw new ArgumentOutOfRangeException("height");
		}
		if (fps < 1 || fps > 60)
		{
			throw new ArgumentOutOfRangeException("fps");
		}
		if (jpegFrames == null)
		{
			throw new ArgumentNullException("jpegFrames");
		}
		int count = jpegFrames.Count;
		if (count < 1 || count > 900)
		{
			throw new ArgumentOutOfRangeException("jpegFrames", "A clip must contain 1–900 frames.");
		}
		byte[][] array = new byte[count][];
		int num = 0;
		long num2 = 232 + (long)count * 24L;
		for (int i = 0; i < count; i++)
		{
			byte[] array2 = jpegFrames[i];
			if (array2 == null || array2.Length == 0)
			{
				throw new ArgumentException("A JPEG frame is empty.", "jpegFrames");
			}
			array[i] = array2;
			num = Math.Max(num, array2.Length);
			num2 += array2.Length + (array2.Length & 1);
			if (num2 > 268435456)
			{
				throw new ArgumentException("The AVI clip exceeds its 256 MiB limit.", "jpegFrames");
			}
		}
		using BinaryWriter binaryWriter = new BinaryWriter(destination, Encoding.ASCII, leaveOpen: true);
		long sizePosition = Begin(binaryWriter, "RIFF");
		FourCc(binaryWriter, "AVI ");
		long sizePosition2 = BeginList(binaryWriter, "hdrl");
		long sizePosition3 = Begin(binaryWriter, "avih");
		binaryWriter.Write((uint)(1000000 / fps));
		binaryWriter.Write((uint)Math.Min(4294967295L, (long)num * (long)fps));
		binaryWriter.Write(0u);
		binaryWriter.Write(16u);
		binaryWriter.Write((uint)count);
		binaryWriter.Write(0u);
		binaryWriter.Write(1u);
		binaryWriter.Write((uint)num);
		binaryWriter.Write((uint)width);
		binaryWriter.Write((uint)height);
		for (int j = 0; j < 4; j++)
		{
			binaryWriter.Write(0u);
		}
		End(binaryWriter, sizePosition3);
		long sizePosition4 = BeginList(binaryWriter, "strl");
		long sizePosition5 = Begin(binaryWriter, "strh");
		FourCc(binaryWriter, "vids");
		FourCc(binaryWriter, "MJPG");
		binaryWriter.Write(0u);
		binaryWriter.Write((ushort)0);
		binaryWriter.Write((ushort)0);
		binaryWriter.Write(0u);
		binaryWriter.Write(1u);
		binaryWriter.Write((uint)fps);
		binaryWriter.Write(0u);
		binaryWriter.Write((uint)count);
		binaryWriter.Write((uint)num);
		binaryWriter.Write(uint.MaxValue);
		binaryWriter.Write(0u);
		binaryWriter.Write((short)0);
		binaryWriter.Write((short)0);
		binaryWriter.Write((short)width);
		binaryWriter.Write((short)height);
		End(binaryWriter, sizePosition5);
		long sizePosition6 = Begin(binaryWriter, "strf");
		binaryWriter.Write(40u);
		binaryWriter.Write(width);
		binaryWriter.Write(height);
		binaryWriter.Write((ushort)1);
		binaryWriter.Write((ushort)24);
		FourCc(binaryWriter, "MJPG");
		binaryWriter.Write((uint)(width * height * 3));
		binaryWriter.Write(0);
		binaryWriter.Write(0);
		binaryWriter.Write(0u);
		binaryWriter.Write(0u);
		End(binaryWriter, sizePosition6);
		End(binaryWriter, sizePosition4);
		End(binaryWriter, sizePosition2);
		long sizePosition7 = Begin(binaryWriter, "LIST");
		long position = destination.Position;
		FourCc(binaryWriter, "movi");
		uint[] array3 = new uint[count];
		for (int k = 0; k < count; k++)
		{
			byte[] array4 = array[k];
			array3[k] = checked((uint)(destination.Position - position));
			FourCc(binaryWriter, "00dc");
			binaryWriter.Write((uint)array4.Length);
			binaryWriter.Write(array4);
			if ((array4.Length & 1) != 0)
			{
				binaryWriter.Write((byte)0);
			}
		}
		End(binaryWriter, sizePosition7);
		long sizePosition8 = Begin(binaryWriter, "idx1");
		for (int l = 0; l < count; l++)
		{
			FourCc(binaryWriter, "00dc");
			binaryWriter.Write(16u);
			binaryWriter.Write(array3[l]);
			binaryWriter.Write((uint)array[l].Length);
		}
		End(binaryWriter, sizePosition8);
		End(binaryWriter, sizePosition);
	}

	private static long Begin(BinaryWriter writer, string id)
	{
		FourCc(writer, id);
		long position = writer.BaseStream.Position;
		writer.Write(0u);
		return position;
	}

	private static long BeginList(BinaryWriter writer, string type)
	{
		long result = Begin(writer, "LIST");
		FourCc(writer, type);
		return result;
	}

	private static void End(BinaryWriter writer, long sizePosition)
	{
		long position = writer.BaseStream.Position;
		writer.BaseStream.Position = sizePosition;
		writer.Write(checked((uint)(position - sizePosition - 4)));
		writer.BaseStream.Position = position;
	}

	private static void FourCc(BinaryWriter writer, string text)
	{
		if (text.Length != 4)
		{
			throw new ArgumentException("A FourCC must have four ASCII characters.", "text");
		}
		for (int i = 0; i < 4; i++)
		{
			writer.Write((byte)text[i]);
		}
	}
}
