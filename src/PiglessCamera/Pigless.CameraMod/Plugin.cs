using System;
using Pigless.Interaction;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using GorillaLocomotion;
using Pigless.Sheesh;
using Pigless.Visuals;
using UnityEngine;
using Valve.VR;

namespace Pigless.CameraMod;

[BepInPlugin("local.piglesscamera", "Pigless Camera", "1.4.0")]
[DefaultExecutionOrder(31000)]
public sealed class Plugin : BaseUnityPlugin
{
	private readonly DoublePressToggle doubleY = new DoublePressToggle();

	private readonly DoublePressToggle doubleX = new DoublePressToggle();



	private readonly PressEdge photoEdge = new PressEdge();

	private CameraSettings settings;

	private CameraRig rig;

	private readonly PhotoCountdown photoCountdown = new PhotoCountdown();
	private readonly TouchLatch touch = new TouchLatch();
	private readonly ButtonSound buttonSound = new ButtonSound();

	private CameraView view;




	private ConfigEntry<float> tabletScale;

	private ConfigEntry<bool> tabletMigrated;

	private SteamVR_Action_Boolean yAction;

	private SteamVR_Action_Boolean xAction;



	private SteamVR_Action_Boolean bAction;

	private SteamVR_Action_Boolean leftGripAction;

	private SteamVR_Action_Boolean rightGripAction;

	private bool open;

	private bool settingsOpen;

	private bool placed;

	private bool ready;

	private GrabHand grabHand;

	private int settingsPage;

	private Vector3 gripOffset;

	private Quaternion gripRotation;

	private float progress;

	private float toastUntil;

	private float nextError;

	private float nextGripHint;


	private string toast;


	private TabletAction hovered;

	public bool IsPanelOpen => open;

	private void Awake()
	{
		settings = new CameraSettings(base.Config);
		ConfigEntry<bool> configEntry = base.Config.Bind("General", "LevelViewsUpgradeApplied", defaultValue: false, "Records the one-time removal of hidden roll settings from older camera menus.");
		if (!configEntry.Value)
		{
			settings.Roll.Value = 0f;
			settings.HorizonLock.Value = true;
			configEntry.Value = true;
			base.Logger.LogInfo("Cleared old hidden roll settings; automatic views keep a level horizon.");
		}
		tabletScale = base.Config.Bind("Tablet", "Size", 0.95f, new ConfigDescription("Physical camera tablet size.", new AcceptableValueRange<float>(0.7f, 1.2f)));
		tabletMigrated = base.Config.Bind("Tablet", "SimpleControlsInitialized", defaultValue: false, "Remembers the one-time upgrade to the physical camera controls.");
		rig = new CameraRig(settings, base.Logger);
		base.Logger.LogInfo("Pigless Camera 1.4.0 loaded. Double Y opens the tablet. Either grip grabs it. Touch buttons with the free hand. Photos have a two-second countdown. Theme #8E5500 / white.");
	}

	private void Update()
	{
		bool pressed = false;
		bool pressed2 = false;
		bool pressed5 = false;
		bool flag = false;
		bool flag2 = false;
		bool flag3 = false;
		try
		{
			if (SteamVR_Input.initialized && !SteamVR_Input.isStartupFrame)
			{
				if ((SteamVR_Action)(object)yAction == (SteamVR_Action)null)
				{
					yAction = SteamVR_Actions.gorillaTag_LeftSecondaryClick;
				}
				if ((SteamVR_Action)(object)xAction == (SteamVR_Action)null)
				{
					xAction = SteamVR_Actions.gorillaTag_LeftPrimaryClick;
				}
				if ((SteamVR_Action)(object)bAction == (SteamVR_Action)null)
				{
					bAction = SteamVR_Actions.gorillaTag_RightSecondaryClick;
				}
				if ((SteamVR_Action)(object)leftGripAction == (SteamVR_Action)null)
				{
					leftGripAction = SteamVR_Actions.gorillaTag_LeftGripClick;
				}
				if ((SteamVR_Action)(object)rightGripAction == (SteamVR_Action)null)
				{
					rightGripAction = SteamVR_Actions.gorillaTag_RightGripClick;
				}
				bool flag4 = (SteamVR_Action)(object)yAction != (SteamVR_Action)null && ((SteamVR_Action)yAction).GetActive((SteamVR_Input_Sources)1);
				flag3 = flag4;
				if (flag3)
				{
					pressed = flag4 && yAction.GetState((SteamVR_Input_Sources)1);
					pressed2 = (SteamVR_Action)(object)xAction != (SteamVR_Action)null && xAction.GetState((SteamVR_Input_Sources)1);
					pressed5 = (SteamVR_Action)(object)bAction != (SteamVR_Action)null && bAction.GetState((SteamVR_Input_Sources)2);
					flag = (SteamVR_Action)(object)leftGripAction != (SteamVR_Action)null && leftGripAction.GetState((SteamVR_Input_Sources)1);
					flag2 = (SteamVR_Action)(object)rightGripAction != (SteamVR_Action)null && rightGripAction.GetState((SteamVR_Input_Sources)2);
				}
			}
		}
		catch (Exception error)
		{
			LogError("Camera controller input failed", error);
		}
		GorillaTagger instance = GorillaTagger.Instance;
		GTPlayer instance2 = GTPlayer.Instance;
		ready = flag3 && (UnityEngine.Object)(object)instance != null && instance.leftHandTransform != null && instance.rightHandTransform != null && (UnityEngine.Object)(object)instance2 != null && instance2.mainCamera != null;
		if (doubleY.Poll(ready, pressed, Time.unscaledTime))
		{
			TogglePanel();
		}
		if (doubleX.Poll(ready, pressed2, Time.unscaledTime) && open)
		{
			ClosePanel();
		}
		bool flag7 = photoEdge.Poll(ready, pressed5);
		if (!ready)
		{
			ClosePanel();
			return;
		}
		if (flag7)
		{
			Photo();
		}
		if (!open || view == null)
		{
			return;
		}
		if (progress > 0.9f)
		{
			GrabHand grabHand = CameraGrab.Select(this.grabHand, flag, flag2, DistanceToTablet(instance.leftHandTransform), DistanceToTablet(instance.rightHandTransform), 0.3f);
			if (grabHand != this.grabHand && grabHand != GrabHand.None)
			{
				Transform transform = ((grabHand == GrabHand.Left) ? instance.leftHandTransform : instance.rightHandTransform);
				gripOffset = transform.InverseTransformPoint(view.Root.position);
				gripRotation = Quaternion.Inverse(transform.rotation) * view.Root.rotation;
				instance.StartVibration(grabHand == GrabHand.Left, 0.1f, 0.035f);
			}
			this.grabHand = grabHand;
			if (this.grabHand == GrabHand.None && (flag || flag2) && Time.unscaledTime >= nextGripHint)
			{
				nextGripHint = Time.unscaledTime + 4f;
				ShowToast("MOVE YOUR HAND CLOSER TO GRAB THE CAMERA");
			}
		}
		if (this.grabHand != GrabHand.None)
		{
			Transform transform2 = ((this.grabHand == GrabHand.Left) ? instance.leftHandTransform : instance.rightHandTransform);
			view.Root.SetPositionAndRotation(transform2.TransformPoint(gripOffset), transform2.rotation * gripRotation);
		}
		if (progress > 0.9f)
		{
			SelectTouch((this.grabHand == GrabHand.Right) ? instance.leftHandTransform : instance.rightHandTransform);
		}
	}

	private float DistanceToTablet(Transform hand)
	{
		Vector3 vector = view.Root.InverseTransformPoint(hand.position);
		Vector3 position = new Vector3(Mathf.Clamp(vector.x, -0.47f, 0.47f), Mathf.Clamp(vector.y, -0.25f, 0.25f), Mathf.Clamp(vector.z, -0.065f, 0.065f));
		return Vector3.Distance(hand.position, view.Root.TransformPoint(position));
	}

	public void TogglePanel()
	{
		if (open)
		{
			ClosePanel();
			return;
		}
		GTPlayer instance = GTPlayer.Instance;
		GorillaTagger instance2 = GorillaTagger.Instance;
		if ((UnityEngine.Object)(object)instance == null || instance.mainCamera == null || (UnityEngine.Object)(object)instance2 == null)
		{
			base.Logger.LogInfo("Camera tablet is waiting for the local player.");
			return;
		}
		try
		{
			EnsureView();
			RecallTablet(instance.mainCamera.transform);
			if (!tabletMigrated.Value)
			{
				settings.Mode.Value = CameraMode.Handheld;
				settings.DesktopOutput.Value = true;
				settings.AspectPreset.Value = 0;
				settings.ShowInterface.Value = false;
				settings.Guides.Value = false;
				settings.ShowModel.Value = true;
				tabletMigrated.Value = true;
			}
			settings.Enabled.Value = true;
			open = true;
			settingsOpen = false;
			settingsPage = 0;
			grabHand = GrabHand.None;
			view.ShowSettings(visible: false, settingsPage);
			if (Chainloader.PluginInfos.TryGetValue("local.piglesssheesh", out var value) && value.Instance != null)
			{
				value.Instance.GetType().GetMethod("CloseMenu", Type.EmptyTypes)?.Invoke(value.Instance, null);
			}
			ShowToast("READY - TOUCH BUTTONS WITH YOUR FREE HAND");
			base.Logger.LogInfo("Pigless Camera tablet OPEN. Desktop feed " + (settings.DesktopOutput.Value ? "ON" : "OFF") + ".");
			base.Logger.LogInfo("Camera grips active: left=" + ((SteamVR_Action)(object)leftGripAction != (SteamVR_Action)null && ((SteamVR_Action)leftGripAction).GetActive((SteamVR_Input_Sources)1)) + "; right=" + ((SteamVR_Action)(object)rightGripAction != (SteamVR_Action)null && ((SteamVR_Action)rightGripAction).GetActive((SteamVR_Input_Sources)2)) + ".");
		}
		catch (Exception error)
		{
			ClosePanel();
			LogError("Camera tablet could not open", error);
		}
	}

	public void ClosePanel()
	{
		if (open)
		{
			base.Logger.LogInfo("Pigless Camera tablet CLOSED; filming output continues.");
		}
		open = false;
		grabHand = GrabHand.None;
		settingsOpen = false;
		hovered = TabletAction.None;
		view?.ShowSettings(visible: false);
		touch.Reset();
	}

	private void EnsureView()
    {
        if (view == null) view = new CameraView(base.Logger);
    }

	private void RecallTablet(Transform head)
	{
		Vector3 vector = Vector3.ProjectOnPlane(head.forward, Vector3.up);
		if (vector.sqrMagnitude < 0.001f)
		{
			vector = Vector3.forward;
		}
		else
		{
			vector.Normalize();
		}
		Vector3 vector2 = head.position + vector * 0.72f + Vector3.down * 0.16f;
		view.Root.SetPositionAndRotation(vector2, Quaternion.LookRotation(vector2 - head.position, Vector3.up));
		view.Root.localScale = Vector3.one * tabletScale.Value;
		placed = true;
		grabHand = GrabHand.None;
	}

	private void LateUpdate()
	{
		GTPlayer instance = GTPlayer.Instance;
		if (settings.Enabled.Value && view == null && (UnityEngine.Object)(object)instance != null && instance.mainCamera != null)
		{
			try
			{
				EnsureView();
				RecallTablet(instance.mainCamera.transform);
			}
			catch (Exception error)
			{
				settings.Enabled.Value = false;
				LogError("Camera tablet initialization failed", error);
			}
		}
		if (view != null && !placed && (UnityEngine.Object)(object)instance != null && instance.mainCamera != null)
		{
			RecallTablet(instance.mainCamera.transform);
		}
		rig.Tick(instance, view?.Lens, Time.unscaledDeltaTime);
		rig.DrawDesktop();
        if (photoCountdown.Poll(Time.unscaledTime))
        {
            rig.TakePhoto();
            ShowToast(rig.Status);
        }
        else if (photoCountdown.Pending)
        {
            toast = "PHOTO IN " + photoCountdown.Remaining(Time.unscaledTime);
            toastUntil = Time.unscaledTime + 0.2f;
        }
        if (view == null) return;
		progress = Mathf.MoveTowards(progress, open ? 1f : 0f, Time.unscaledDeltaTime / 0.18f);
		bool flag = progress > 0.001f;
		view.Root.gameObject.SetActive(flag);
		if (!flag)
		{
			view.Root.localScale = Vector3.one * tabletScale.Value;
			return;
		}
		view.Root.localScale = Vector3.one * tabletScale.Value * (0.94f + 0.06f * MenuAnimation.Ease(progress));
		view.Refresh(rig, settings, tabletScale.Value, hovered, (Time.unscaledTime < toastUntil) ? toast : null, settingsPage);
    }

    private void SelectTouch(Transform hand)
    {
        if (view == null || hand == null) { touch.Reset(); return; }
        Vector3 point = view.Root.InverseTransformPoint(hand.position);
        bool overSettings = settingsOpen && TabletControls.SettingsBounds.Contains(point.x, point.y);
        float surfaceZ = overSettings ? -0.078f : -0.054f;
        // Match the rendered front face, with a shallow physical contact volume.
        bool contact = Math.Abs(point.z - surfaceZ) <= 0.025f;
        hovered = contact ? TabletControls.HitTest(point.x, point.y, settingsOpen, settingsPage) : TabletAction.None;
        if (!touch.Poll((int)hovered)) return;
        try
        {
            Activate(hovered);
            try { buttonSound.Play(hand.position); }
            catch (Exception audioError) { base.Logger.LogWarning("Button sound unavailable: " + audioError.Message); }
            GorillaTagger.Instance?.StartVibration(grabHand == GrabHand.Right, 0.1f, 0.035f);
        }
        catch (Exception error)
        {
            ShowToast("ACTION FAILED - CHECK LOG");
            LogError("Camera touch action failed", error);
        }
    }

	private void Activate(TabletAction action)
	{
		CameraMode? cameraMode = null;
		switch (action)
		{
		case TabletAction.FirstPerson:
			cameraMode = CameraMode.FirstPerson;
			break;
		case TabletAction.ThirdPerson:
			cameraMode = CameraMode.Selfie;
			break;
		case TabletAction.GoPro:
			cameraMode = CameraMode.GoPro;
			break;
		case TabletAction.Follow:
			cameraMode = CameraMode.Follow;
			break;
		case TabletAction.Track:
			cameraMode = CameraMode.Track;
			break;
		case TabletAction.Lock:
			cameraMode = CameraMode.Tripod;
			break;
		case TabletAction.Flip:
			rig.ToggleFlip();
			ShowToast(settings.Flipped.Value ? "FLIPPED VIEW" : "FORWARD VIEW");
			break;
		case TabletAction.Photo:
			Photo();
			break;
		case TabletAction.Desktop:
			settings.DesktopOutput.Value = !settings.DesktopOutput.Value;
			if (settings.DesktopOutput.Value)
			{
				rig.RetryDesktop();
			}
			ShowToast(settings.DesktopOutput.Value ? "STARTING THE DESKTOP CAMERA" : "DESKTOP OFF - NORMAL GAME VIEW");
			break;
		case TabletAction.Settings:
			settingsOpen = !settingsOpen;
			view.ShowSettings(settingsOpen, settingsPage);
			break;
		case TabletAction.SettingsBasics:
			settingsPage = 0;
			view.ShowSettings(visible: true, settingsPage);
			break;
		case TabletAction.SettingsDisplay:
			settingsPage = 1;
			view.ShowSettings(visible: true, settingsPage);
			break;
		case TabletAction.Done:
			settingsOpen = false;
			view.ShowSettings(visible: false);
			break;
		case TabletAction.RollLock:
			settings.RollLock.Value = !settings.RollLock.Value;
			break;
		case TabletAction.ZoomOut:
			settings.Fov.Value = Mathf.Clamp(settings.Fov.Value + 5f, 30f, 110f);
			break;
		case TabletAction.ZoomIn:
			settings.Fov.Value = Mathf.Clamp(settings.Fov.Value - 5f, 30f, 110f);
			break;
		case TabletAction.Smoothing:
			settings.Smoothing.Value = ((settings.Smoothing.Value <= 0f) ? 10 : ((!(settings.Smoothing.Value < 7f)) ? 4 : 0));
			break;
		case TabletAction.Quality:
			settings.ResolutionPreset.Value = (Mathf.Clamp(settings.ResolutionPreset.Value, 0, 2) + 1) % 3;
			break;
		case TabletAction.Mirror:
			settings.Mirror.Value = !settings.Mirror.Value;
			break;
		case TabletAction.Size:
			tabletScale.Value = ((tabletScale.Value < 0.85f) ? 0.95f : ((tabletScale.Value < 1.05f) ? 1.15f : 0.75f));
			break;
		}
		if (cameraMode.HasValue)
		{
			CameraMode cameraMode2 = ((settings.Mode.Value == cameraMode.Value) ? CameraMode.Handheld : cameraMode.Value);
			rig.SetMode(cameraMode2);
			settingsOpen = false;
			view.ShowSettings(visible: false);
			ShowToast(CameraRig.ModeLabel(cameraMode2) + ((cameraMode2 == CameraMode.Handheld) ? " - EITHER GRIP TO MOVE" : " - TAP AGAIN FOR FREE CAMERA"));
		}
	}

	private void Photo()
	{
		if (!settings.Enabled.Value || rig.SceneCamera == null || !rig.SceneCamera.enabled)
        { ShowToast("ENABLE CAMERA BEFORE TAKING A PHOTO"); return; }
        if (photoCountdown.Start(Time.unscaledTime)) ShowToast("PHOTO IN 2");
	}

	private void ShowToast(string message)
	{
		toast = message;
		toastUntil = Time.unscaledTime + 3f;
	}

	private void LogError(string label, Exception error)
	{
		if (Time.unscaledTime >= nextError)
		{
			nextError = Time.unscaledTime + 5f;
			base.Logger.LogError(label + ": " + error);
		}
	}

	private void OnDisable()
	{
		ClosePanel();
		progress = 0f;
		doubleY.Reset();
		doubleX.Reset();
		photoEdge.Reset();
		if (view != null)
		{
			view.Root.gameObject.SetActive(value: false);
		}
		rig?.Pause();
		photoCountdown.Cancel();
		touch.Reset();
	}

	private void OnDestroy()
	{
		buttonSound.Dispose();
		rig?.Dispose();
		view?.Dispose();
	}
}
