using UnityEngine;

namespace Pigless.Sheesh;

internal struct InputFrame
{
	internal bool Available;

	internal bool X;

	internal bool StickClick;

	internal bool Trigger;

	internal bool LeftGrip;

	internal bool RightGrip;

	internal Vector2 Axis;
}
