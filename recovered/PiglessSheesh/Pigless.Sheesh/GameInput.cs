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
			if (x == null)
			{
				x = SteamVR_Actions.gorillaTag_LeftPrimaryClick;
			}
			if (stick == null)
			{
				stick = SteamVR_Actions.gorillaTag_LeftJoystickClick;
			}
			if (trigger == null)
			{
				trigger = SteamVR_Actions.gorillaTag_RightTriggerClick;
			}
			if (leftGrip == null)
			{
				leftGrip = SteamVR_Actions.gorillaTag_LeftGripClick;
			}
			if (rightGrip == null)
			{
				rightGrip = SteamVR_Actions.gorillaTag_RightGripClick;
			}
			if (axis == null)
			{
				axis = SteamVR_Actions.gorillaTag_LeftJoystick2DAxis;
			}
			if (x == null || !x.GetActive(SteamVR_Input_Sources.LeftHand))
			{
				Status = "Left X action is inactive; waiting for controller binding.";
				return default(InputFrame);
			}
			Status = "SteamVR left X is active. Double-press X to open Pigless Sheesh.";
			return new InputFrame
			{
				Available = true,
				X = x.GetState(SteamVR_Input_Sources.LeftHand),
				StickClick = (stick != null && stick.GetState(SteamVR_Input_Sources.LeftHand)),
				Trigger = (trigger != null && trigger.GetState(SteamVR_Input_Sources.RightHand)),
				LeftGrip = (leftGrip != null && leftGrip.GetState(SteamVR_Input_Sources.LeftHand)),
				RightGrip = (rightGrip != null && rightGrip.GetState(SteamVR_Input_Sources.RightHand)),
				Axis = ((axis != null && axis.GetActive(SteamVR_Input_Sources.LeftHand)) ? Vector2.ClampMagnitude(axis.GetAxis(SteamVR_Input_Sources.LeftHand), 1f) : Vector2.zero)
			};
		}
		catch (Exception ex)
		{
			Status = "SteamVR input unavailable: " + ex.GetType().Name + ": " + ex.Message;
			return default(InputFrame);
		}
	}
}
