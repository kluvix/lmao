using System;

namespace Pigless.CameraMod;

internal static class CameraMath
{
	internal static void GetResolution(int quality, int aspect, int maximum, out int width, out int height)
	{
		int num = ((quality <= 0) ? 720 : ((quality == 1) ? 1080 : 1440));
		int num2 = num * 16 / 9;
		switch (Math.Max(0, Math.Min(3, aspect)))
		{
		case 1:
			width = num;
			height = num2;
			break;
		case 2:
			width = num;
			height = num;
			break;
		case 3:
			width = num2;
			height = (int)Math.Round((double)num2 / 2.35);
			break;
		default:
			width = num2;
			height = num;
			break;
		}
		maximum = Math.Max(1, maximum);
		double num3 = Math.Min(1.0, (double)maximum / (double)Math.Max(width, height));
		width = Math.Max(1, (int)Math.Round((double)width * num3));
		height = Math.Max(1, (int)Math.Round((double)height * num3));
	}

	internal static FrameRect FitFrame(float screenWidth, float screenHeight, float imageWidth, float imageHeight)
	{
		if (!FinitePositive(screenWidth) || !FinitePositive(screenHeight) || !FinitePositive(imageWidth) || !FinitePositive(imageHeight))
		{
			return new FrameRect(0f, 0f, 0f, 0f);
		}
		float num = Math.Min(screenWidth / imageWidth, screenHeight / imageHeight);
		float num2 = imageWidth * num;
		float num3 = imageHeight * num;
		return new FrameRect((screenWidth - num2) * 0.5f, (screenHeight - num3) * 0.5f, num2, num3);
	}

	internal static float Damping(float responsiveness, float deltaTime)
	{
		if (float.IsNaN(responsiveness) || responsiveness <= 0f)
		{
			return 1f;
		}
		if (float.IsNaN(deltaTime) || deltaTime <= 0f)
		{
			return 0f;
		}
		return (float)(1.0 - Math.Exp((0f - Math.Min(100f, responsiveness)) * Math.Min(1f, deltaTime)));
	}

	internal static float DollyProgress(float elapsed, float duration, bool loop, out bool complete)
	{
		duration = ((float.IsNaN(duration) || float.IsInfinity(duration)) ? 8f : Math.Max(0.1f, duration));
		elapsed = ((float.IsNaN(elapsed) || float.IsInfinity(elapsed)) ? 0f : Math.Max(0f, elapsed));
		float num = elapsed / duration;
		complete = !loop && num >= 1f;
		float num2 = (loop ? (num % 2f) : Math.Min(1f, num));
		if (loop && num2 > 1f)
		{
			num2 = 2f - num2;
		}
		return num2 * num2 * (3f - 2f * num2);
	}

	private static bool FinitePositive(float value)
	{
		if (value > 0f)
		{
			return !float.IsInfinity(value);
		}
		return false;
	}
}
