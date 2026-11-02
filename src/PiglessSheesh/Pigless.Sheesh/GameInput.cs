using System;
using UnityEngine;
using Valve.VR;

namespace Pigless.Sheesh;

internal sealed class GameInput
{
	private SteamVR_Action_Boolean x;

	private SteamVR_Action_Boolean stick;

	private SteamVR_Action_Boolean trigger;

	private SteamVR_Action_Boolean leftGrip;

	private SteamVR_Action_Boolean rightGrip;

	private SteamVR_Action_Vector2 axis;

	internal string Status { get; private set; } = "Waiting for SteamVR.";

	internal InputFrame Read()
	{
		try
		{
			if (!SteamVR_Input.initialized || SteamVR_Input.isStartupFrame)
			{
				return default(InputFrame);
			}
			if ((SteamVR_Action)(object)x == (SteamVR_Action)null)
			{
				x = SteamVR_Actions.gorillaTag_LeftPrimaryClick;
			}
			if ((SteamVR_Action)(object)stick == (SteamVR_Action)null)
			{
				stick = SteamVR_Actions.gorillaTag_LeftJoystickClick;
			}
			if ((SteamVR_Action)(object)trigger == (SteamVR_Action)null)
			{
				trigger = SteamVR_Actions.gorillaTag_RightTriggerClick;
			}
			if ((SteamVR_Action)(object)leftGrip == (SteamVR_Action)null)
			{
				leftGrip = SteamVR_Actions.gorillaTag_LeftGripClick;
			}
			if ((SteamVR_Action)(object)rightGrip == (SteamVR_Action)null)
			{
				rightGrip = SteamVR_Actions.gorillaTag_RightGripClick;
			}
			if ((SteamVR_Action)(object)axis == (SteamVR_Action)null)
			{
				axis = SteamVR_Actions.gorillaTag_LeftJoystick2DAxis;
			}
			if ((SteamVR_Action)(object)x == (SteamVR_Action)null || !((SteamVR_Action)x).GetActive((SteamVR_Input_Sources)1))
			{
				Status = "Left X action is inactive; waiting for controller binding.";
				return default(InputFrame);
			}
			Status = "SteamVR left X is active. Double-press X to open Pigless Sheesh.";
			return new InputFrame
			{
				Available = true,
				X = x.GetState((SteamVR_Input_Sources)1),
				StickClick = ((SteamVR_Action)(object)stick != (SteamVR_Action)null && stick.GetState((SteamVR_Input_Sources)1)),
				Trigger = ((SteamVR_Action)(object)trigger != (SteamVR_Action)null && trigger.GetState((SteamVR_Input_Sources)2)),
				LeftGrip = ((SteamVR_Action)(object)leftGrip != (SteamVR_Action)null && leftGrip.GetState((SteamVR_Input_Sources)1)),
				RightGrip = ((SteamVR_Action)(object)rightGrip != (SteamVR_Action)null && rightGrip.GetState((SteamVR_Input_Sources)2)),
				Axis = (((SteamVR_Action)(object)axis != (SteamVR_Action)null && ((SteamVR_Action)axis).GetActive((SteamVR_Input_Sources)1)) ? Vector2.ClampMagnitude(axis.GetAxis((SteamVR_Input_Sources)1), 1f) : Vector2.zero)
			};
		}
		catch (Exception ex)
		{
			Status = "SteamVR input unavailable: " + ex.GetType().Name + ": " + ex.Message;
			return default(InputFrame);
		}
	}
}
