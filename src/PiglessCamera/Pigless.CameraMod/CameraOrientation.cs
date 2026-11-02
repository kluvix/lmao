using System;
using UnityEngine;

namespace Pigless.CameraMod;

internal static class CameraOrientation
{
	internal static Vector3 Heading(Vector3 direction, Vector3 fallback)
	{
		if (TryUnit(new Vector3(direction.x, 0f, direction.z), out var result))
		{
			return result;
		}
		if (!TryUnit(new Vector3(fallback.x, 0f, fallback.z), out result))
		{
			return Vector3.forward;
		}
		return result;
	}

	internal static Vector3 HeadPosition(Vector3 headPosition, Vector3 heading, bool flipped)
	{
		if (!flipped)
		{
			return headPosition;
		}
		return headPosition + Heading(heading, Vector3.forward) * 1.2f;
	}

	internal static Quaternion Aim(Vector3 direction, Vector3 suppliedUp, Vector3 fallbackForward, bool rollLock)
	{
		if (rollLock)
		{
			return Level(direction, fallbackForward);
		}
		if (!TryUnit(direction, out var result))
		{
			result = (TryUnit(fallbackForward, out var result2) ? result2 : Vector3.forward);
		}
		if (!TryUnit(suppliedUp, out var result3) || Vector3.Cross(result, result3).sqrMagnitude < 1E-06f)
		{
			return Level(result, fallbackForward);
		}
		return Quaternion.LookRotation(result, result3);
	}

	internal static Quaternion ApplyRollLock(Quaternion rotation, Vector3 fallbackForward, bool rollLock)
	{
		if (!rollLock)
		{
			return rotation;
		}
		return Level(rotation * Vector3.forward, fallbackForward);
	}

	internal static Quaternion Reverse(Quaternion rotation)
	{
		return rotation * Quaternion.AngleAxis(180f, Vector3.up);
	}

	internal static Quaternion Level(Vector3 direction, Vector3 fallbackForward)
	{
		if (!TryUnit(fallbackForward, out var result))
		{
			result = Vector3.forward;
		}
		if (!TryUnit(direction, out var result2))
		{
			result2 = result;
		}
		Vector3 result3 = Vector3.Cross(Vector3.up, result2);
		if (result3.sqrMagnitude < 0.0001f)
		{
			Vector3 result4 = new Vector3(result.x, 0f, result.z);
			if (!TryUnit(result4, out result4))
			{
				result4 = Vector3.forward;
			}
			result3 = Vector3.Cross(Vector3.up, result4);
		}
		TryUnit(result3, out result3);
		Vector3 upwards = Vector3.Cross(result2, result3);
		return Quaternion.LookRotation(result2, upwards);
	}

	private static bool TryUnit(Vector3 value, out Vector3 result)
	{
		if (!Finite(value.x) || !Finite(value.y) || !Finite(value.z))
		{
			result = Vector3.zero;
			return false;
		}
		double num = Math.Sqrt((double)value.x * (double)value.x + (double)value.y * (double)value.y + (double)value.z * (double)value.z);
		if (num < 1E-06)
		{
			result = Vector3.zero;
			return false;
		}
		result = new Vector3((float)((double)value.x / num), (float)((double)value.y / num), (float)((double)value.z / num));
		return true;
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
