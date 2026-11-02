namespace Pigless.Sheesh;

internal static class MenuAnimation
{
	internal static float Ease(float progress)
	{
		if (float.IsNaN(progress) || progress <= 0f)
		{
			return 0f;
		}
		if (progress >= 1f)
		{
			return 1f;
		}
		float num = progress - 1f;
		return 1f + 2.25f * num * num * num + 1.25f * num * num;
	}
}
