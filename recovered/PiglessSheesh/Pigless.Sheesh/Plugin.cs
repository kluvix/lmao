using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using GorillaLocomotion;
using LookFlight;
using Pigless.Visuals;
using UnityEngine;

namespace Pigless.Sheesh;

[BepInPlugin("local.piglesssheesh", "Pigless Sheesh", "2.2.0")]
[BepInDependency("local.lookflight", BepInDependency.DependencyFlags.SoftDependency)]
[DefaultExecutionOrder(30000)]
public sealed class Plugin : BaseUnityPlugin
{
	private sealed class Entry
	{
		internal string Title;

		internal string Hint;

		internal Func<string> State;

		internal Func<bool> Active;

		internal Action Click;
	}

	private sealed class Button
	{
		internal Vector2 Center;

		internal Vector2 Size;

		internal Renderer Panel;

		internal Component Title;

		internal Component Hint;

		internal Component Status;

		internal int Row = -1;

		internal int Page = -1;

		internal bool Enabled = true;

		internal Action Click;
	}

	private readonly GameInput input = new GameInput();

	private readonly DoublePressToggle doubleX = new DoublePressToggle();

	private readonly PressEdge stickPress = new PressEdge();

	private readonly PressEdge triggerPress = new PressEdge();

	private readonly List<Button> buttons = new List<Button>();

	private readonly List<Entry[]> pages = new List<Entry[]>();

	private readonly string[] pageNames = new string[5] { "MOVEMENT", "VISUALS", "TOOLS", "STYLE", "TAGS" };

	private ConfigEntry<float> speed;

	private ConfigEntry<float> menuScale;

	private ConfigEntry<float> platformSize;

	private ConfigEntry<float> pointerLength;

	private ConfigEntry<bool> pinned;

	private ConfigEntry<bool> haptics;

	private ConfigEntry<bool> animate;

	private ConfigEntry<bool> platformVisible;

	private FlightSession flight;

	private Platforms platforms;

	private VisualTools tools;

	private TagTools tags;

	private WorldUi menuUi;

	private WorldUi pointerUi;

	private Transform laser;

	private Transform cursor;

	private Component pageLabel;

	private Component footerLabel;

	private InputFrame frame;

	private bool menuOpen;

	private bool flyArmed;

	private bool platformsEnabled;

	private bool hoverHold;

	private int page;

	private float progress;

	private float nextErrorLog;

	private float toastUntil;

	private string lastInputStatus;

	private string toast;

	private BaseUnityPlugin cameraPlugin;

	private PropertyInfo cameraPanelOpen;

	private void Awake()
	{
		speed = base.Config.Bind("Flight", "Speed", 12f, new ConfigDescription("Flight speed in metres/second.", new AcceptableValueRange<float>(1f, 20f)));
		menuScale = base.Config.Bind("Menu", "Scale", 1f, new ConfigDescription("Menu size multiplier.", new AcceptableValueRange<float>(0.75f, 1.4f)));
		pinned = base.Config.Bind("Menu", "Pinned", defaultValue: false, "Keep the open panel anchored in world space.");
		haptics = base.Config.Bind("Menu", "Haptics", defaultValue: true, "Controller click feedback.");
		animate = base.Config.Bind("Menu", "Animation", defaultValue: true, "Animate menu opening and closing.");
		pointerLength = base.Config.Bind("Menu", "PointerLength", 2f, new ConfigDescription("Right-hand pointer range in metres.", new AcceptableValueRange<float>(1f, 3f)));
		platformSize = base.Config.Bind("Platforms", "Size", 0.32f, new ConfigDescription("Platform width/depth in metres.", new AcceptableValueRange<float>(0.22f, 0.55f)));
		platformVisible = base.Config.Bind("Platforms", "Visible", defaultValue: true, "Draw the platforms; turning this off leaves enabled platform collisions in place.");
		if (Chainloader.PluginInfos.TryGetValue("local.lookflight", out var value) && value.Instance != null && value.Instance.enabled)
		{
			value.Instance.enabled = false;
			base.Logger.LogInfo("Standalone Look Flight disabled for this launch; Pigless Sheesh owns flight.");
		}
		flight = new FlightSession(this, base.Logger);
		tools = new VisualTools(base.Logger);
		tags = new TagTools(base.Logger);
		BuildEntries();
		base.Logger.LogInfo("Pigless Sheesh 2.2.0 loaded. Double X opens the menu; right trigger selects. TAGS: hold right grip and press trigger for the gun. Manual flight OFF keeps momentum. Theme #8E5500 / white. Features start OFF.");
	}

	private Entry Item(string title, string hint, Func<string> state, Func<bool> active, Action click)
	{
		return new Entry
		{
			Title = title,
			Hint = hint,
			State = state,
			Active = active,
			Click = click
		};
	}

	private static string On(bool value)
	{
		if (!value)
		{
			return "OFF";
		}
		return "ON";
	}

	private void BuildEntries()
	{
		pages.Add(new Entry[6]
		{
			Item("FLY", "Look to steer / left stick to move", () => (!flight.Active) ? ((!flyArmed) ? "OFF" : "READY") : "ACTIVE", () => flyArmed, ToggleFly),
			Item("FLIGHT SPEED", "Cycle 4 / 8 / 12 / 18 / 20 m/s", () => speed.Value.ToString("0") + " m/s", () => false, delegate
			{
				speed.Value = Next(speed.Value, 4f, 8f, 12f, 18f, 20f);
			}),
			Item("HOVER LOCK", "Freeze movement while flight runs", () => On(hoverHold), () => hoverHold, delegate
			{
				hoverHold = !hoverHold;
			}),
			Item("PLATFORMS", "Hold grips for anchored hand platforms", () => On(platformsEnabled), () => platformsEnabled, TogglePlatforms),
			Item("PLATFORM SIZE", "Cycle small / normal / large", () => platformSize.Value.ToString("0.00") + " m", () => false, delegate
			{
				platformSize.Value = Next(platformSize.Value, 0.24f, 0.32f, 0.45f);
				platforms?.SetSize(platformSize.Value);
			}),
			Item("PLATFORM GRAPHICS", "Hide/show the enabled platform surfaces", () => On(platformVisible.Value), () => platformVisible.Value, delegate
			{
				platformVisible.Value = !platformVisible.Value;
				platforms?.SetVisible(platformVisible.Value);
			})
		});
		pages.Add(new Entry[6]
		{
			Item("HAND TRAILS", "A local trail follows both controllers", () => On(tools.Trails), () => tools.Trails, delegate
			{
				tools.Trails = !tools.Trails;
			}),
			Item("TRAIL LENGTH", "Cycle short / medium / long", () => (new string[3] { "SHORT", "MEDIUM", "LONG" })[Mathf.Clamp(tools.TrailPreset, 0, 2)], () => false, delegate
			{
				tools.TrailPreset = (tools.TrailPreset + 1) % 3;
			}),
			Item("HAND LIGHTS", "Light the nearby space around your hands", () => On(tools.HandLights), () => tools.HandLights, delegate
			{
				tools.HandLights = !tools.HandLights;
			}),
			Item("HAND MARKERS", "Local controller position markers", () => On(tools.Markers), () => tools.Markers, delegate
			{
				tools.Markers = !tools.Markers;
			}),
			Item("CLEAR HAND EFFECTS", "Turn off trails, lights and markers", () => "CLEAR", () => false, delegate
			{
				tools.Trails = (tools.HandLights = (tools.Markers = false));
			}),
			Item("RESET FEATURES", "Stop flight, platforms, effects and stats", () => "RESET", () => false, ResetFeatures)
		});
		pages.Add(new Entry[6]
		{
			Item("FPS DISPLAY", "Frame-rate readout on the wrist HUD", () => On(tools.ShowFps), () => tools.ShowFps, delegate
			{
				tools.ShowFps = !tools.ShowFps;
			}),
			Item("SPEEDOMETER", "Show local movement speed", () => On(tools.ShowSpeed), () => tools.ShowSpeed, delegate
			{
				tools.ShowSpeed = !tools.ShowSpeed;
			}),
			Item("SESSION TIMER", "Show elapsed time in this session", () => On(tools.ShowTimer), () => tools.ShowTimer, delegate
			{
				tools.ShowTimer = !tools.ShowTimer;
			}),
			Item("COORDINATES", "Show your local world position", () => On(tools.ShowCoordinates), () => tools.ShowCoordinates, delegate
			{
				tools.ShowCoordinates = !tools.ShowCoordinates;
			}),
			Item("CAMERA CONTROLS", "Open the separate Pigless Camera panel", () => (!Chainloader.PluginInfos.ContainsKey("local.piglesscamera")) ? "INSTALL" : "OPEN", () => false, OpenCamera),
			Item("CLEAR WRIST HUD", "Hide every wrist statistics readout", () => "CLEAR", () => false, delegate
			{
				tools.ShowFps = (tools.ShowSpeed = (tools.ShowTimer = (tools.ShowCoordinates = false)));
			})
		});
		pages.Add(new Entry[6]
		{
			Item("MENU SIZE", "Cycle compact / normal / large / extra", () => menuScale.Value.ToString("0.00") + "x", () => false, delegate
			{
				menuScale.Value = Next(menuScale.Value, 0.85f, 1f, 1.15f, 1.3f);
			}),
			Item("PIN MENU", "Keep the panel where it is in the world", () => On(pinned.Value), () => pinned.Value, delegate
			{
				pinned.Value = !pinned.Value;
			}),
			Item("CLICK HAPTICS", "Controller feedback for selections", () => On(haptics.Value), () => haptics.Value, delegate
			{
				haptics.Value = !haptics.Value;
			}),
			Item("OPEN ANIMATION", "Switch animated / instant opening", () => On(animate.Value), () => animate.Value, delegate
			{
				animate.Value = !animate.Value;
			}),
			Item("POINTER RANGE", "Cycle the right-hand pointer reach", () => pointerLength.Value.ToString("0.0") + " m", () => false, delegate
			{
				pointerLength.Value = Next(pointerLength.Value, 1.2f, 2f, 3f);
			}),
			Item("RESET ALL", "Restore defaults and stop every feature", () => "RESET", () => false, ResetAll)
		});
		pages.Add(new Entry[2]
		{
			Item("TAG GUN", "Close menu / hold right grip / trigger", () => On(tags.GunEnabled), () => tags.GunEnabled, delegate
			{
				tags.GunEnabled = !tags.GunEnabled;
				toast = (tags.GunEnabled ? "CLOSE MENU / RIGHT GRIP + TRIGGER" : "TAG GUN OFF");
				toastUntil = Time.unscaledTime + 4f;
			}),
			Item("TAG ALL", "Room host / infection mode / one pass", () => "TAG", () => false, delegate
			{
				tags.TagAll();
				toast = tags.Status;
				toastUntil = Time.unscaledTime + 5f;
			})
		});
	}

	private static float Next(float current, params float[] values)
	{
		foreach (float num in values)
		{
			if (num > current + 0.001f)
			{
				return num;
			}
		}
		return values[0];
	}

	private void Update()
	{
		frame = input.Read();
		if (lastInputStatus != input.Status)
		{
			lastInputStatus = input.Status;
			base.Logger.LogInfo(input.Status);
		}
		GorillaTagger instance = GorillaTagger.Instance;
		GTPlayer instance2 = GTPlayer.Instance;
		bool flag = frame.Available && instance != null && instance2 != null && instance2.mainCamera != null && instance.leftHandTransform != null && instance.rightHandTransform != null;
		bool flag2 = doubleX.Poll(flag, frame.X, Time.unscaledTime);
		bool flag3 = stickPress.Poll(flag, frame.StickClick);
		bool press = triggerPress.Poll(flag, frame.Trigger);
		if (!flag)
		{
			flight.Stop();
			platforms?.Clear();
			platformsEnabled = false;
			menuOpen = false;
			tags.Tick(available: false, menuOpen: true, instance2, null, trigger: false, rightGrip: false);
			return;
		}
		if (flight.Active && !flight.IsValid)
		{
			flight.Stop();
		}
		if (flag2)
		{
			try
			{
				EnsureUi();
				menuOpen = !menuOpen;
				if (menuOpen)
				{
					Transform transform = instance2.mainCamera.transform;
					menuUi.Root.position = MenuPosition(instance.leftHandTransform, transform);
					menuUi.Root.rotation = Quaternion.LookRotation(menuUi.Root.position - transform.position, transform.up);
				}
				Buzz(left: true);
				base.Logger.LogInfo(menuOpen ? "Pigless Sheesh menu OPEN." : "Pigless Sheesh menu CLOSED.");
			}
			catch (Exception ex)
			{
				menuOpen = false;
				base.Logger.LogError("Menu creation failed: " + ex);
			}
		}
		if (flyArmed && flag3)
		{
			if (flight.Active)
			{
				flight.Stop(preserveMomentum: true);
			}
			else
			{
				flight.TryStart(instance2);
			}
			Buzz(left: true);
		}
		if (platformsEnabled)
		{
			if (platforms == null)
			{
				platforms = new Platforms(base.Logger);
				platforms.SetSize(platformSize.Value);
				platforms.SetVisible(platformVisible.Value);
			}
			platforms.Tick(enabled: true, frame.LeftGrip, frame.RightGrip, instance.leftHandTransform, instance.rightHandTransform);
		}
		else
		{
			platforms?.Clear();
		}
		if (menuUi != null && menuOpen && progress > 0.9f)
		{
			Select(instance.rightHandTransform, press);
		}
	}

	private void FixedUpdate()
	{
		if (flight == null || !flight.Active)
		{
			return;
		}
		if (!flight.IsValid || !frame.Available)
		{
			flight.Stop();
			return;
		}
		Camera headCamera = flight.HeadCamera;
		if (headCamera == null)
		{
			flight.Stop();
			return;
		}
		float num = Mathf.Clamp01(frame.Axis.magnitude);
		Vector3 velocity = Vector3.zero;
		if (!hoverHold && num > 0.15f)
		{
			velocity = (headCamera.transform.forward * frame.Axis.y + headCamera.transform.right * frame.Axis.x).normalized * speed.Value * ((num - 0.15f) / 0.85f);
		}
		flight.SetVelocity(velocity);
	}

	private void LateUpdate()
	{
		GTPlayer instance = GTPlayer.Instance;
		GorillaTagger instance2 = GorillaTagger.Instance;
		tags?.Tick(frame.Available, menuOpen || CameraPanelIsOpen(), instance, (instance2 != null) ? instance2.rightHandTransform : null, frame.Trigger, frame.RightGrip);
		tools?.Tick(instance, (frame.Available && instance2 != null) ? instance2.leftHandTransform : null, (frame.Available && instance2 != null) ? instance2.rightHandTransform : null, Time.unscaledDeltaTime);
		if (menuUi == null)
		{
			return;
		}
		progress = (animate.Value ? Mathf.MoveTowards(progress, menuOpen ? 1f : 0f, Time.unscaledDeltaTime / 0.24f) : (menuOpen ? 1f : 0f));
		bool flag = progress > 0.001f;
		menuUi.Root.gameObject.SetActive(flag);
		pointerUi.Root.gameObject.SetActive(flag && menuOpen && progress > 0.9f);
		if (!flag)
		{
			return;
		}
		menuUi.RefreshLayout();
		if (instance == null || instance.mainCamera == null || instance2 == null || instance2.leftHandTransform == null)
		{
			menuOpen = false;
			return;
		}
		if (!pinned.Value)
		{
			Transform transform = instance.mainCamera.transform;
			Vector3 vector = MenuPosition(instance2.leftHandTransform, transform);
			float t = 1f - Mathf.Exp(-22f * Time.unscaledDeltaTime);
			menuUi.Root.position = Vector3.Lerp(menuUi.Root.position, vector, t);
			menuUi.Root.rotation = Quaternion.Slerp(menuUi.Root.rotation, Quaternion.LookRotation(vector - transform.position, transform.up), t);
		}
		menuUi.Root.localScale = Vector3.one * menuScale.Value * (animate.Value ? MenuAnimation.Ease(progress) : progress);
		RefreshButtons(-1);
		if (menuOpen && progress > 0.9f && instance2.rightHandTransform != null)
		{
			Select(instance2.rightHandTransform, press: false);
		}
	}

	private static Vector3 MenuPosition(Transform hand, Transform head)
	{
		return hand.position + head.right * 0.13f + Vector3.up * 0.19f + head.forward * 0.11f;
	}

	private void EnsureUi()
	{
		if (menuUi != null)
		{
			return;
		}
		try
		{
			menuUi = new WorldUi("Pigless Sheesh / wrist menu", base.Logger);
			menuUi.Root.gameObject.layer = 5;
			UnityEngine.Object.DontDestroyOnLoad(menuUi.Root.gameObject);
			menuUi.Panel("Back plate", new Vector2(0.382f, 0.522f), 0.028f, 0.018f, Vector3.zero, Theme.Dark);
			menuUi.Panel("Bronze rim", new Vector2(0.374f, 0.514f), 0.026f, 0.007f, new Vector3(0f, 0f, -0.012f), Theme.Rim);
			menuUi.Panel("Brown face", new Vector2(0.366f, 0.506f), 0.024f, 0.006f, new Vector3(0f, 0f, -0.017f), Theme.Brown);
			menuUi.Panel("Brand badge", new Vector2(0.042f, 0.04f), 0.01f, 0.004f, new Vector3(-0.142f, 0.21f, -0.022f), Theme.Dark);
			menuUi.Label("PS", new Vector3(-0.142f, 0.21f, -0.028f), 0.015f, Theme.White, "MiddleCenter", 0.033f);
			menuUi.Label("PIGLESS SHEESH", new Vector3(-0.008f, 0.215f, -0.028f), 0.023f, Theme.White, "MiddleCenter", 0.23f);
			pageLabel = menuUi.Label(pageNames[page], new Vector3(-0.008f, 0.181f, -0.028f), 0.011f, Theme.White, "MiddleCenter", 0.23f);
			AddAuxButton("X", new Vector2(0.151f, 0.21f), new Vector2(0.035f, 0.032f), CloseMenu);
			menuUi.Label("POINT RIGHT HAND + PRESS TRIGGER", new Vector3(0f, 0.139f, -0.028f), 0.008f, Theme.White, "MiddleCenter", 0.33f);
			for (int i = 0; i < 6; i++)
			{
				AddFeatureButton(i, new Vector2(0f, 0.097f - (float)i * 0.05f));
			}
			string[] array = new string[5] { "MOVE", "VISUALS", "TOOLS", "STYLE", "TAGS" };
			for (int j = 0; j < array.Length; j++)
			{
				int target = j;
				AddAuxButton(array[j], new Vector2(-0.136f + (float)j * 0.068f, -0.21f), new Vector2(0.064f, 0.03f), delegate
				{
					SetPage(target);
				}).Page = j;
			}
			footerLabel = menuUi.Label("DOUBLE X TO CLOSE", new Vector3(0f, -0.242f, -0.028f), 0.008f, Theme.White, "MiddleCenter", 0.33f);
			SetPage(page);
			menuUi.Root.localScale = Vector3.zero;
			menuUi.Root.gameObject.SetActive(value: false);
			pointerUi = new WorldUi("Pigless Sheesh / pointer", base.Logger);
			pointerUi.Root.gameObject.layer = 5;
			UnityEngine.Object.DontDestroyOnLoad(pointerUi.Root.gameObject);
			laser = pointerUi.Primitive("Pointer beam", PrimitiveType.Cylinder, Vector3.zero, Vector3.one, Theme.White).transform;
			cursor = pointerUi.Primitive("Pointer dot", PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.007f, Theme.White).transform;
			pointerUi.Root.gameObject.SetActive(value: false);
		}
		catch
		{
			menuUi?.Dispose();
			menuUi = null;
			pointerUi?.Dispose();
			pointerUi = null;
			buttons.Clear();
			throw;
		}
	}

	private void AddFeatureButton(int row, Vector2 center)
	{
		Vector2 size = new Vector2(0.332f, 0.045f);
		Button button = new Button
		{
			Center = center,
			Size = size,
			Row = row
		};
		button.Panel = menuUi.Panel("Feature row", size, 0.01f, 0.005f, new Vector3(center.x, center.y, -0.024f), Theme.Button);
		button.Title = menuUi.Label("", new Vector3(-0.149f, center.y + 0.009f, -0.03f), 0.012f, Theme.White, "MiddleLeft", 0.225f);
		button.Hint = menuUi.Label("", new Vector3(-0.149f, center.y - 0.012f, -0.03f), 0.0075f, Theme.White, "MiddleLeft", 0.302f);
		button.Status = menuUi.Label("", new Vector3(0.15f, center.y + 0.009f, -0.03f), 0.009f, Theme.White, "MiddleRight", 0.07f);
		button.Click = delegate
		{
			pages[page][row].Click();
		};
		buttons.Add(button);
	}

	private Button AddAuxButton(string title, Vector2 center, Vector2 size, Action click)
	{
		Button button = new Button
		{
			Center = center,
			Size = size,
			Click = click
		};
		button.Panel = menuUi.Panel(title, size, 0.009f, 0.005f, new Vector3(center.x, center.y, -0.024f), Theme.Button);
		button.Title = menuUi.Label(title, new Vector3(center.x, center.y, -0.03f), 0.009f, Theme.White, "MiddleCenter", size.x - 0.009f);
		buttons.Add(button);
		return button;
	}

	private void SetPage(int target)
	{
		page = Mathf.Clamp(target, 0, pages.Count - 1);
		if (menuUi == null)
		{
			return;
		}
		menuUi.SetText(pageLabel, pageNames[page]);
		foreach (Button button in buttons)
		{
			if (button.Row >= 0)
			{
				button.Enabled = button.Row < pages[page].Length;
				button.Panel.gameObject.SetActive(button.Enabled);
				button.Title.gameObject.SetActive(button.Enabled);
				button.Hint.gameObject.SetActive(button.Enabled);
				button.Status.gameObject.SetActive(button.Enabled);
				if (button.Enabled)
				{
					Entry entry = pages[page][button.Row];
					menuUi.SetText(button.Title, entry.Title);
					menuUi.SetText(button.Hint, entry.Hint);
				}
			}
		}
		RefreshButtons(-1);
	}

	private void RefreshButtons(int hovered)
	{
		for (int i = 0; i < buttons.Count; i++)
		{
			Button button = buttons[i];
			if (button.Enabled)
			{
				bool flag = ((button.Page >= 0) ? (button.Page == page) : (button.Row >= 0 && pages[page][button.Row].Active()));
				menuUi.SetColor(button.Panel, (i == hovered) ? Theme.Hover : (flag ? Theme.Active : Theme.Button));
				if (button.Row >= 0)
				{
					menuUi.SetText(button.Status, pages[page][button.Row].State());
				}
			}
		}
		if (footerLabel != null)
		{
			menuUi.SetText(footerLabel, (Time.unscaledTime < toastUntil) ? toast : ((page == 4) ? tags.Status : "DOUBLE X TO CLOSE"));
		}
	}

	private void Select(Transform hand, bool press)
	{
		Vector3 vector = hand.position + hand.forward * 0.025f;
		Vector3 forward = hand.forward;
		Plane plane = new Plane(menuUi.Root.forward, menuUi.Root.TransformPoint(new Vector3(0f, 0f, -0.034f)));
		Ray ray = new Ray(vector, forward);
		int num = -1;
		float num2 = Mathf.Clamp(pointerLength.Value, 1f, 3f);
		Vector3 vector2 = vector + forward * num2;
		if (plane.Raycast(ray, out var enter) && enter > 0f && enter < num2)
		{
			vector2 = ray.GetPoint(enter);
			Vector3 vector3 = menuUi.Root.InverseTransformPoint(vector2);
			for (int i = 0; i < buttons.Count; i++)
			{
				Button button = buttons[i];
				if (button.Enabled && Mathf.Abs(vector3.x - button.Center.x) <= button.Size.x * 0.5f && Mathf.Abs(vector3.y - button.Center.y) <= button.Size.y * 0.5f)
				{
					num = i;
					break;
				}
			}
		}
		Vector3 vector4 = vector2 - vector;
		laser.position = (vector + vector2) * 0.5f;
		laser.rotation = Quaternion.FromToRotation(Vector3.up, vector4.normalized);
		laser.localScale = new Vector3(0.0018f, vector4.magnitude * 0.5f, 0.0018f);
		cursor.position = vector2;
		cursor.localScale = Vector3.one * ((num >= 0) ? 0.01f : 0.006f);
		RefreshButtons(num);
		if (!press || num < 0)
		{
			return;
		}
		try
		{
			buttons[num].Click();
			Buzz(left: false);
		}
		catch (Exception ex)
		{
			if (Time.unscaledTime >= nextErrorLog)
			{
				nextErrorLog = Time.unscaledTime + 5f;
				base.Logger.LogError("Menu action failed: " + ex);
			}
		}
	}

	private void ToggleFly()
	{
		if (flyArmed)
		{
			flyArmed = false;
			flight.Stop(preserveMomentum: true);
		}
		else
		{
			flyArmed = flight.TryStart(GTPlayer.Instance);
		}
	}

	private void TogglePlatforms()
	{
		platformsEnabled = !platformsEnabled;
		if (!platformsEnabled)
		{
			platforms?.Clear();
		}
	}

	private void OpenCamera()
	{
		if (Chainloader.PluginInfos.TryGetValue("local.piglesscamera", out var value) && value.Instance != null)
		{
			MethodInfo method = value.Instance.GetType().GetMethod("TogglePanel", Type.EmptyTypes);
			if (method != null)
			{
				method.Invoke(value.Instance, null);
				CloseMenu();
				return;
			}
		}
		toast = "INSTALL PIGLESS CAMERA FIRST";
		toastUntil = Time.unscaledTime + 4f;
	}

	public void CloseMenu()
	{
		menuOpen = false;
	}

	private bool CameraPanelIsOpen()
	{
		if (tags == null || !tags.GunEnabled)
		{
			return false;
		}
		if (!Chainloader.PluginInfos.TryGetValue("local.piglesscamera", out var value) || value.Instance == null || !value.Instance.enabled)
		{
			return false;
		}
		if (cameraPlugin != value.Instance)
		{
			cameraPlugin = value.Instance;
			cameraPanelOpen = cameraPlugin.GetType().GetProperty("IsPanelOpen");
		}
		bool flag = default(bool);
		int num;
		if (cameraPanelOpen != null)
		{
			object value2 = cameraPanelOpen.GetValue(cameraPlugin, null);
			if (value2 is bool)
			{
				flag = (bool)value2;
				num = 1;
			}
			else
			{
				num = 0;
			}
		}
		else
		{
			num = 0;
		}
		return (byte)((uint)num & (flag ? 1u : 0u)) != 0;
	}

	private void ResetFeatures()
	{
		flyArmed = (platformsEnabled = (hoverHold = false));
		flight?.Stop();
		platforms?.Clear();
		tools?.Reset();
		tags?.Reset();
	}

	private void ResetAll()
	{
		ResetFeatures();
		speed.Value = 12f;
		menuScale.Value = 1f;
		pinned.Value = false;
		haptics.Value = true;
		animate.Value = true;
		platformSize.Value = 0.32f;
		platformVisible.Value = true;
		pointerLength.Value = 2f;
		platforms?.SetSize(0.32f);
		platforms?.SetVisible(visible: true);
		toast = "ALL FEATURES RESET";
		toastUntil = Time.unscaledTime + 3f;
	}

	private void Buzz(bool left)
	{
		if (haptics.Value)
		{
			GorillaTagger instance = GorillaTagger.Instance;
			if (instance != null)
			{
				instance.StartVibration(left, 0.15f, 0.045f);
			}
		}
	}

	private void OnDisable()
	{
		doubleX.Reset();
		stickPress.Reset();
		triggerPress.Reset();
		menuOpen = false;
		ResetFeatures();
		menuUi?.Root.gameObject.SetActive(value: false);
		pointerUi?.Root.gameObject.SetActive(value: false);
	}

	private void OnDestroy()
	{
		flight?.Stop();
		platforms?.Dispose();
		tools?.Dispose();
		tags?.Dispose();
		menuUi?.Dispose();
		pointerUi?.Dispose();
	}
}
