using System;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using GorillaLocomotion;
using Pigless.Sheesh;
using Pigless.Visuals;
using UnityEngine;
using Valve.VR;

namespace Pigless.CameraMod;

[BepInPlugin("local.piglesscamera", "Pigless Camera", "1.3.0")]
[DefaultExecutionOrder(31000)]
public sealed class Plugin : BaseUnityPlugin
{
	private readonly DoublePressToggle doubleY = new DoublePressToggle();

	private readonly DoublePressToggle doubleX = new DoublePressToggle();

	private readonly PressEdge triggerEdge = new PressEdge();

	private readonly PressEdge leftTriggerEdge = new PressEdge();

	private readonly PressEdge photoEdge = new PressEdge();

	private CameraSettings settings;

	private CameraRig rig;

	private ClipRecorder clips;

	private CameraView view;

	private WorldUi pointer;

	private Transform beam;

	private Transform dot;

	private ConfigEntry<float> tabletScale;

	private ConfigEntry<bool> tabletMigrated;

	private SteamVR_Action_Boolean yAction;

	private SteamVR_Action_Boolean xAction;

	private SteamVR_Action_Boolean triggerAction;

	private SteamVR_Action_Boolean leftTriggerAction;

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

	private float nextClipCaption;

	private string toast;

	private string lastClipResult;

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
		clips = new ClipRecorder(base.Logger);
		base.Logger.LogInfo("Pigless Camera 1.3.0 loaded. Double Y opens the tablet. Either grip grabs it. FLIP reverses the view; Roll Lock is in BASICS. CLIP saves recent silent video. Theme #8E5500 / white.");
	}

	private void Update()
	{
		bool pressed = false;
		bool pressed2 = false;
		bool pressed3 = false;
		bool pressed4 = false;
		bool pressed5 = false;
		bool flag = false;
		bool flag2 = false;
		bool flag3 = false;
		try
		{
			if (SteamVR_Input.initialized && !SteamVR_Input.isStartupFrame)
			{
				if (yAction == null)
				{
					yAction = SteamVR_Actions.gorillaTag_LeftSecondaryClick;
				}
				if (xAction == null)
				{
					xAction = SteamVR_Actions.gorillaTag_LeftPrimaryClick;
				}
				if (triggerAction == null)
				{
					triggerAction = SteamVR_Actions.gorillaTag_RightTriggerClick;
				}
				if (leftTriggerAction == null)
				{
					leftTriggerAction = SteamVR_Actions.gorillaTag_LeftTriggerClick;
				}
				if (bAction == null)
				{
					bAction = SteamVR_Actions.gorillaTag_RightSecondaryClick;
				}
				if (leftGripAction == null)
				{
					leftGripAction = SteamVR_Actions.gorillaTag_LeftGripClick;
				}
				if (rightGripAction == null)
				{
					rightGripAction = SteamVR_Actions.gorillaTag_RightGripClick;
				}
				bool flag4 = yAction != null && yAction.GetActive(SteamVR_Input_Sources.LeftHand);
				flag3 = flag4 || (triggerAction != null && triggerAction.GetActive(SteamVR_Input_Sources.RightHand));
				if (flag3)
				{
					pressed = flag4 && yAction.GetState(SteamVR_Input_Sources.LeftHand);
					pressed2 = xAction != null && xAction.GetState(SteamVR_Input_Sources.LeftHand);
					pressed3 = triggerAction != null && triggerAction.GetState(SteamVR_Input_Sources.RightHand);
					pressed4 = leftTriggerAction != null && leftTriggerAction.GetState(SteamVR_Input_Sources.LeftHand);
					pressed5 = bAction != null && bAction.GetState(SteamVR_Input_Sources.RightHand);
					flag = leftGripAction != null && leftGripAction.GetState(SteamVR_Input_Sources.LeftHand);
					flag2 = rightGripAction != null && rightGripAction.GetState(SteamVR_Input_Sources.RightHand);
				}
			}
		}
		catch (Exception error)
		{
			LogError("Camera controller input failed", error);
		}
		GorillaTagger instance = GorillaTagger.Instance;
		GTPlayer instance2 = GTPlayer.Instance;
		ready = flag3 && instance != null && instance.leftHandTransform != null && instance.rightHandTransform != null && instance2 != null && instance2.mainCamera != null;
		if (doubleY.Poll(ready, pressed, Time.unscaledTime))
		{
			TogglePanel();
		}
		if (doubleX.Poll(ready, pressed2, Time.unscaledTime) && open)
		{
			ClosePanel();
		}
		bool flag5 = triggerEdge.Poll(ready, pressed3);
		bool flag6 = leftTriggerEdge.Poll(ready, pressed4);
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
			Select((this.grabHand == GrabHand.Right) ? instance.leftHandTransform : instance.rightHandTransform, (this.grabHand == GrabHand.Right) ? flag6 : flag5);
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
		if (instance == null || instance.mainCamera == null || instance2 == null)
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
			ShowToast("READY - POINT YOUR FREE HAND AND PRESS TRIGGER");
			base.Logger.LogInfo("Pigless Camera tablet OPEN. Desktop feed " + (settings.DesktopOutput.Value ? "ON" : "OFF") + ".");
			base.Logger.LogInfo("Camera grips active: left=" + (leftGripAction != null && leftGripAction.GetActive(SteamVR_Input_Sources.LeftHand)) + "; right=" + (rightGripAction != null && rightGripAction.GetActive(SteamVR_Input_Sources.RightHand)) + ".");
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
		pointer?.Root.gameObject.SetActive(value: false);
	}

	private void EnsureView()
	{
		if (view == null)
		{
			view = new CameraView(base.Logger);
		}
		if (pointer != null)
		{
			return;
		}
		try
		{
			pointer = new WorldUi("Pigless Camera / tablet pointer", base.Logger);
			pointer.Root.gameObject.layer = 2;
			UnityEngine.Object.DontDestroyOnLoad(pointer.Root.gameObject);
			beam = pointer.Primitive("Pointer beam", PrimitiveType.Cylinder, Vector3.zero, Vector3.one, Theme.White).transform;
			dot = pointer.Primitive("Pointer dot", PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.006f, Theme.White).transform;
			pointer.Root.gameObject.SetActive(value: false);
		}
		catch
		{
			pointer?.Dispose();
			pointer = null;
			throw;
		}
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
		if (settings.Enabled.Value && view == null && instance != null && instance.mainCamera != null)
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
		if (view != null && !placed && instance != null && instance.mainCamera != null)
		{
			RecallTablet(instance.mainCamera.transform);
		}
		rig.Tick(instance, view?.Lens, Time.unscaledDeltaTime);
		rig.DrawDesktop();
		clips.Tick(rig.OutputTexture, settings.Enabled.Value && rig.SceneCamera != null && rig.SceneCamera.enabled, settings.Mirror.Value, Time.unscaledTime);
		if (clips.Status != lastClipResult)
		{
			lastClipResult = clips.Status;
			if (lastClipResult.StartsWith("CLIP SAVED", StringComparison.Ordinal) || lastClipResult.StartsWith("CLIP FAILED", StringComparison.Ordinal))
			{
				ShowToast(lastClipResult);
			}
		}
		if (view == null)
		{
			return;
		}
		if (Time.unscaledTime >= nextClipCaption)
		{
			nextClipCaption = Time.unscaledTime + 0.25f;
			view.SetClipStatus(clips.IsSaving ? "SAVING" : (clips.Status.StartsWith("CLIP FAILED", StringComparison.Ordinal) ? "RETRY" : ((clips.BufferedSeconds < 0.5) ? "WARMING" : ("CLIP " + clips.BufferedSeconds.ToString("0") + "s"))));
		}
		progress = Mathf.MoveTowards(progress, open ? 1f : 0f, Time.unscaledDeltaTime / 0.18f);
		bool flag = progress > 0.001f;
		view.Root.gameObject.SetActive(flag);
		if (pointer != null)
		{
			pointer.Root.gameObject.SetActive(open && ready && progress > 0.9f);
		}
		if (!flag)
		{
			view.Root.localScale = Vector3.one * tabletScale.Value;
			return;
		}
		view.Root.localScale = Vector3.one * tabletScale.Value * (0.94f + 0.06f * MenuAnimation.Ease(progress));
		view.Refresh(rig, settings, tabletScale.Value, hovered, (Time.unscaledTime < toastUntil) ? toast : null, settingsPage);
		GorillaTagger instance2 = GorillaTagger.Instance;
		if (open && ready && progress > 0.9f && instance2 != null && instance2.rightHandTransform != null)
		{
			Select((grabHand == GrabHand.Right) ? instance2.leftHandTransform : instance2.rightHandTransform, click: false);
		}
	}

	private void Select(Transform hand, bool click)
	{
		if (pointer == null || view == null)
		{
			return;
		}
		Vector3 vector = hand.position + hand.forward * 0.025f;
		Ray ray = new Ray(vector, hand.forward);
		Vector3 vector2 = vector + hand.forward * 2.5f;
		hovered = TabletAction.None;
		if (Vector3.Dot(ray.direction, view.Root.forward) > 0f)
		{
			bool flag = false;
			if (settingsOpen && Intersect(ray, -0.078f, out var point))
			{
				Vector3 vector3 = view.Root.InverseTransformPoint(point);
				if (TabletControls.SettingsBounds.Contains(vector3.x, vector3.y))
				{
					vector2 = point;
					flag = true;
					hovered = TabletControls.HitTest(vector3.x, vector3.y, settingsOpen: true, settingsPage);
				}
			}
			if (!flag && Intersect(ray, -0.054f, out var point2))
			{
				vector2 = point2;
				Vector3 vector4 = view.Root.InverseTransformPoint(point2);
				hovered = TabletControls.HitTest(vector4.x, vector4.y, settingsOpen: false);
			}
		}
		Vector3 vector5 = vector2 - vector;
		beam.position = (vector + vector2) * 0.5f;
		beam.rotation = Quaternion.FromToRotation(Vector3.up, vector5.normalized);
		beam.localScale = new Vector3(0.0012f, vector5.magnitude * 0.5f, 0.0012f);
		dot.position = vector2;
		dot.localScale = Vector3.one * ((hovered == TabletAction.None) ? 0.0045f : 0.007f);
		if (!click || hovered == TabletAction.None)
		{
			return;
		}
		try
		{
			Activate(hovered);
			GorillaTagger.Instance?.StartVibration(grabHand == GrabHand.Right, 0.1f, 0.035f);
		}
		catch (Exception error)
		{
			ShowToast("ACTION FAILED - CHECK LOG");
			LogError("Camera tablet action failed", error);
		}
	}

	private bool Intersect(Ray ray, float localZ, out Vector3 point)
	{
		if (new Plane(view.Root.forward, view.Root.TransformPoint(new Vector3(0f, 0f, localZ))).Raycast(ray, out var enter) && enter > 0f && enter < 2.5f)
		{
			point = ray.GetPoint(enter);
			return true;
		}
		point = default(Vector3);
		return false;
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
		case TabletAction.Clip:
			clips.SaveClip();
			ShowToast(clips.Status);
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
		rig.TakePhoto();
		ShowToast(rig.Status);
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
		triggerEdge.Reset();
		leftTriggerEdge.Reset();
		photoEdge.Reset();
		if (view != null)
		{
			view.Root.gameObject.SetActive(value: false);
		}
		rig?.Pause();
		clips?.Tick(null, enabled: false, mirror: false, Time.unscaledTime);
	}

	private void OnDestroy()
	{
		clips?.Dispose();
		rig?.Dispose();
		view?.Dispose();
		pointer?.Dispose();
	}
}
