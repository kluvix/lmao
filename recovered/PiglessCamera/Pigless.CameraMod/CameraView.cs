using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Pigless.Visuals;
using UnityEngine;

namespace Pigless.CameraMod;

internal sealed class CameraView : IDisposable
{
	private readonly WorldUi ui;

	private readonly Renderer screen;

	private readonly Transform settingsPanel;

	private readonly Transform[] settingsPages = new Transform[2];

	private int settingsPage;

	private readonly Component status;

	private readonly Component modeLabel;

	private readonly Component waiting;

	private readonly Dictionary<TabletAction, Renderer> surfaces = new Dictionary<TabletAction, Renderer>();

	private readonly Dictionary<TabletAction, Component> values = new Dictionary<TabletAction, Component>();

	private readonly Dictionary<TabletAction, Renderer> selectionRims = new Dictionary<TabletAction, Renderer>();

	private readonly List<Vector3> iconVertices = new List<Vector3>();

	private readonly List<int> iconTriangles = new List<int>();

	private static readonly Color Body = new Color(0.025f, 0.029f, 0.037f, 1f);

	private static readonly Color Edge = new Color(0.052f, 0.058f, 0.07f, 1f);

	private static readonly string[] QualityNames = new string[3] { "720p", "1080p", "1440p" };

	private float nextTextRefresh;

	private string previousMessage;

	private Texture lastPreviewTexture;

	private bool previewKnown;

	private bool lastMirror;

	private bool colorsKnown;

	private bool lastDesktop;

	private bool lastSettings;

	private bool lastRollLock;

	private bool lastFlipped;

	private int lastSettingsPage;

	private TabletAction lastSelected;

	private TabletAction lastHovered;

	internal Transform Root => ui.Root;

	internal Transform Lens { get; }

	internal CameraView(ManualLogSource logger)
	{
		ui = new WorldUi("Pigless Camera / camera tablet", logger);
		ui.Root.gameObject.layer = 2;
		UnityEngine.Object.DontDestroyOnLoad(ui.Root.gameObject);
		try
		{
			ui.Panel("Chassis edge", new Vector2(0.924f, 0.482f), 0.045f, 0.038f, Vector3.zero, Edge);
			ui.Panel("Chassis front", new Vector2(0.914f, 0.472f), 0.041f, 0.012f, new Vector3(0f, 0f, -0.022f), Body);
			ui.Panel("Chassis back", new Vector2(0.9f, 0.454f), 0.039f, 0.01f, new Vector3(0f, 0f, 0.022f), Body);
			float[] array = new float[2] { -1f, 1f };
			foreach (float num in array)
			{
				ui.Panel("Handle socket", new Vector2(0.085f, 0.359f), 0.039f, 0.009f, new Vector3(num * 0.365f, 0f, -0.033f), Color.black);
				ui.Panel("Handle inset", new Vector2(0.065f, 0.329f), 0.031f, 0.006f, new Vector3(num * 0.365f, 0f, -0.039f), Edge);
				ui.Panel("Brown grip edge", new Vector2(0.078f, 0.302f), 0.015f, 0.022f, new Vector3(num * 0.425f, 0f, -0.037f), Theme.Button);
				ui.Panel("Brown grip", new Vector2(0.071f, 0.295f), 0.014f, 0.018f, new Vector3(num * 0.428f, 0f, -0.05f), Theme.Brown);
				ui.Panel("Grip highlight", new Vector2(0.004f, 0.267f), 0.002f, 0.001f, new Vector3(num * 0.4f, 0f, -0.06f), Theme.Hover);
			}
			ui.Panel("Screen bevel shadow", new Vector2(0.595f, 0.367f), 0.025f, 0.009f, new Vector3(0f, 0.045f, -0.033f), Color.black);
			ui.Panel("Brown screen bezel", new Vector2(0.58f, 0.352f), 0.022f, 0.008f, new Vector3(0f, 0.045f, -0.04f), Theme.Brown);
			ui.Panel("Screen glass surround", new Vector2(0.547f, 0.308f), 0.012f, 0.006f, new Vector3(0f, 0.025f, -0.046f), Color.black);
			screen = ui.Image("Live camera screen", null, new Vector2(0.535f, 0.301f), new Vector3(0f, 0.025f, -0.051f));
			ui.Panel("Mode name shade", new Vector2(0.26f, 0.026f), 0.005f, 0.002f, new Vector3(0f, 0.155f, -0.055f), new Color(0f, 0f, 0f, 0.82f));
			modeLabel = ui.Label("FREE CAMERA", new Vector3(0f, 0.155f, -0.058f), 0.013f, Theme.White, "MiddleCenter", 0.245f);
			ui.Panel("Screen status shade", new Vector2(0.524f, 0.021f), 0.004f, 0.002f, new Vector3(0f, -0.113f, -0.055f), new Color(0f, 0f, 0f, 0.82f));
			status = ui.Label("STARTING CAMERA", new Vector3(0f, -0.113f, -0.058f), 0.007f, Theme.White, "MiddleCenter", 0.495f);
			waiting = ui.Label("STARTING CAMERA...", new Vector3(0f, 0.035f, -0.058f), 0.016f, Theme.White, "MiddleCenter", 0.4f);
			ui.Label("PIGLESS", new Vector3(0f, 0.232f, -0.032f), 0.008f, Theme.White, "MiddleCenter", 0.2f);
			ui.Label("SQUEEZE EITHER GRIP: MOVE   /   DOUBLE Y: HIDE   /   B: PHOTO", new Vector3(0f, -0.226f, -0.035f), 0.007f, Theme.White, "MiddleCenter", 0.65f);
			TabletAction[] mainActions = TabletControls.MainActions;
			foreach (TabletAction action in mainActions)
			{
				BuildButton(action, null);
			}
			ui.Geometry("White tablet icons", iconVertices.ToArray(), iconTriangles.ToArray(), Theme.White);
			iconVertices.Clear();
			iconTriangles.Clear();
			Lens = ui.Group("Filming lens pose");
			Lens.localPosition = new Vector3(0f, 0.03f, 0.052f);
			ui.Primitive("Back lens ring", PrimitiveType.Cylinder, new Vector3(0f, 0.03f, 0.04f), new Vector3(0.072f, 0.012f, 0.072f), Theme.Brown).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
			ui.Primitive("Back lens glass", PrimitiveType.Cylinder, new Vector3(0f, 0.03f, 0.053f), new Vector3(0.055f, 0.004f, 0.055f), Color.black).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
			settingsPanel = ui.Group("Simple camera settings");
			ui.Panel("Settings shadow", new Vector2(0.506f, 0.296f), 0.014f, 0.004f, new Vector3(0f, 0.026f, -0.061f), Color.black, settingsPanel);
			ui.Panel("Settings face", new Vector2(0.5f, 0.29f), 0.012f, 0.004f, new Vector3(0f, 0.026f, -0.066f), Body, settingsPanel);
			ui.Label("SETTINGS", new Vector3(-0.222f, 0.145f, -0.074f), 0.017f, Theme.White, "MiddleLeft", 0.285f, settingsPanel);
			mainActions = TabletControls.SettingsCommonActions;
			foreach (TabletAction action2 in mainActions)
			{
				BuildButton(action2, settingsPanel);
			}
			settingsPages[0] = ui.Group("Basics settings", settingsPanel);
			settingsPages[1] = ui.Group("Display settings", settingsPanel);
			SettingLabel("ZOOM", 0.05f, settingsPages[0]);
			values[TabletAction.ZoomOut] = ui.Label("70 deg", new Vector3(0.115f, 0.05f, -0.082f), 0.014f, Theme.White, "MiddleCenter", 0.09f, settingsPages[0]);
			SettingLabel("SMOOTHING", -0.012f, settingsPages[0]);
			SettingLabel("ROLL LOCK", -0.074f, settingsPages[0]);
			SettingLabel("QUALITY", 0.05f, settingsPages[1]);
			SettingLabel("MIRROR", -0.012f, settingsPages[1]);
			SettingLabel("TABLET SIZE", -0.074f, settingsPages[1]);
			mainActions = TabletControls.BasicsActions;
			foreach (TabletAction action3 in mainActions)
			{
				BuildButton(action3, settingsPages[0]);
			}
			mainActions = TabletControls.DisplayActions;
			foreach (TabletAction action4 in mainActions)
			{
				BuildButton(action4, settingsPages[1]);
			}
			settingsPages[1].gameObject.SetActive(value: false);
			settingsPanel.gameObject.SetActive(value: false);
			ui.Root.gameObject.SetActive(value: false);
		}
		catch
		{
			ui.Dispose();
			throw;
		}
	}

	private void SettingLabel(string text, float y, Transform parent)
	{
		ui.Label(text, new Vector3(-0.222f, y, -0.082f), 0.015f, Theme.White, "MiddleLeft", 0.205f, parent);
	}

	private void BuildButton(TabletAction action, Transform parent)
	{
		TabletRect rect = TabletControls.GetRect(action);
		bool flag = parent != null;
		bool flag2 = Array.IndexOf(TabletControls.ModeActions, action) >= 0;
		float num = (flag ? (-0.075f) : (-0.049f));
		Vector2 vector = new Vector2(rect.Width, rect.Height);
		if (flag2)
		{
			Renderer renderer = ui.Panel(action.ToString() + " selected outline", vector + Vector2.one * 0.004f, 0.01f, 0.003f, new Vector3(rect.X, rect.Y, num + 0.001f), Theme.White, parent);
			selectionRims[action] = renderer;
			renderer.enabled = false;
		}
		surfaces[action] = ui.Panel(action.ToString() + " button", vector, 0.008f, 0.004f, new Vector3(rect.X, rect.Y, num - 0.002f), Theme.Brown, parent);
		if (flag2)
		{
			DrawIcon(action, new Vector2(rect.X, rect.Y + 0.011f), num - 0.006f, 0.9f, parent);
			ui.Label(Title(action), new Vector3(rect.X, rect.Y - 0.021f, num - 0.007f), 0.014f, Theme.White, "MiddleCenter", rect.Width - 0.009f, parent);
		}
		else if (!flag)
		{
			DrawIcon(action, new Vector2(rect.X, rect.Y + 0.003f), num - 0.007f, (action == TabletAction.Clip) ? 0.85f : 0.65f, parent);
			Component value = ui.Label(Title(action), new Vector3(rect.X, rect.Y - 0.014f, num - 0.008f), (action == TabletAction.Clip) ? 0.01f : 0.008f, Theme.White, "MiddleCenter", rect.Width - 0.006f, parent);
			if (action == TabletAction.Clip)
			{
				values[action] = value;
			}
		}
		else
		{
			Component value2 = ui.Label(Title(action), new Vector3(rect.X, rect.Y, num - 0.008f), (action == TabletAction.Done) ? 0.012f : 0.014f, Theme.White, "MiddleCenter", rect.Width - 0.012f, parent);
			if (action != TabletAction.ZoomOut && action != TabletAction.ZoomIn)
			{
				values[action] = value2;
			}
		}
	}

	private static string Title(TabletAction action)
	{
		return action switch
		{
			TabletAction.FirstPerson => "1ST\nPERSON", 
			TabletAction.ThirdPerson => "3RD\nPERSON", 
			TabletAction.GoPro => "GOPRO\nMODE", 
			TabletAction.Follow => "FOLLOW\nMODE", 
			TabletAction.Track => "TRACK\nMONKE", 
			TabletAction.Lock => "LOCK\nCAMERA", 
			TabletAction.ZoomOut => "-", 
			TabletAction.ZoomIn => "+", 
			TabletAction.Done => "DONE", 
			TabletAction.RollLock => "ROLL LOCK", 
			TabletAction.SettingsBasics => "BASICS", 
			TabletAction.SettingsDisplay => "DISPLAY", 
			_ => action.ToString().ToUpperInvariant(), 
		};
	}

	internal void ShowSettings(bool visible, int page = 0)
	{
		settingsPage = Mathf.Clamp(page, 0, 1);
		settingsPanel.gameObject.SetActive(visible);
		for (int i = 0; i < settingsPages.Length; i++)
		{
			settingsPages[i].gameObject.SetActive(i == settingsPage);
		}
		nextTextRefresh = 0f;
		colorsKnown = false;
	}

	internal void SetClipStatus(string label)
	{
		ui.SetText(values[TabletAction.Clip], string.IsNullOrEmpty(label) ? "CLIP" : label);
	}

	internal void Refresh(CameraRig rig, CameraSettings settings, float scale, TabletAction hovered, string message, int page = 0)
	{
		if (settingsPage != Mathf.Clamp(page, 0, 1))
		{
			ShowSettings(settingsPanel.gameObject.activeSelf, page);
		}
		Texture texture = ((rig.SceneCamera != null && rig.SceneCamera.enabled) ? rig.OutputTexture : null);
		if (!previewKnown || lastPreviewTexture != texture || lastMirror != settings.Mirror.Value)
		{
			Bind(screen, texture, settings.Mirror.Value, new Vector2(0.535f, 0.301f));
			previewKnown = true;
			lastPreviewTexture = texture;
			lastMirror = settings.Mirror.Value;
		}
		bool flag = rig.SceneCamera != null && rig.SceneCamera.enabled && rig.OutputTexture != null;
		waiting.gameObject.SetActive(!flag);
		TabletAction tabletAction = ModeAction(settings.Mode.Value);
		bool activeSelf = settingsPanel.gameObject.activeSelf;
		if (!colorsKnown || lastSelected != tabletAction || lastHovered != hovered || lastDesktop != settings.DesktopOutput.Value || lastSettings != activeSelf || lastRollLock != settings.RollLock.Value || lastFlipped != settings.Flipped.Value || lastSettingsPage != settingsPage)
		{
			foreach (KeyValuePair<TabletAction, Renderer> surface in surfaces)
			{
				bool flag2 = surface.Key == tabletAction || (surface.Key == TabletAction.Desktop && settings.DesktopOutput.Value) || (surface.Key == TabletAction.Settings && activeSelf) || (surface.Key == TabletAction.RollLock && settings.RollLock.Value) || (surface.Key == TabletAction.Flip && settings.Flipped.Value) || (surface.Key == TabletAction.SettingsBasics && settingsPage == 0) || (surface.Key == TabletAction.SettingsDisplay && settingsPage == 1);
				ui.SetColor(surface.Value, (surface.Key == hovered) ? Theme.Hover : (flag2 ? Theme.Active : Theme.Brown));
			}
			foreach (KeyValuePair<TabletAction, Renderer> selectionRim in selectionRims)
			{
				selectionRim.Value.enabled = selectionRim.Key == tabletAction;
			}
			colorsKnown = true;
			lastSelected = tabletAction;
			lastHovered = hovered;
			lastDesktop = settings.DesktopOutput.Value;
			lastSettings = activeSelf;
			lastRollLock = settings.RollLock.Value;
			lastFlipped = settings.Flipped.Value;
			lastSettingsPage = settingsPage;
		}
		if (!(Time.unscaledTime < nextTextRefresh) || !(message == previousMessage))
		{
			nextTextRefresh = Time.unscaledTime + 0.12f;
			previousMessage = message;
			ui.SetText(waiting, settings.Enabled.Value ? rig.Status : "CAMERA PAUSED");
			ui.SetText(modeLabel, CameraRig.ModeLabel(settings.Mode.Value) + (settings.Flipped.Value ? " / FLIPPED" : ""));
			ui.SetText(status, string.IsNullOrEmpty(message) ? (rig.DesktopStatus + "   /   " + (flag ? (rig.OutputTexture.width + " x " + rig.OutputTexture.height) : "WAITING")) : message);
			ui.SetText(values[TabletAction.ZoomOut], settings.Fov.Value.ToString("0") + " deg");
			ui.SetText(values[TabletAction.Smoothing], (settings.Smoothing.Value <= 0f) ? "OFF" : ((settings.Smoothing.Value < 7f) ? "VERY SMOOTH" : "SMOOTH"));
			ui.SetText(values[TabletAction.Quality], QualityNames[Mathf.Clamp(settings.ResolutionPreset.Value, 0, 2)]);
			ui.SetText(values[TabletAction.Mirror], settings.Mirror.Value ? "ON" : "OFF");
			ui.SetText(values[TabletAction.Size], (scale < 0.85f) ? "SMALL" : ((scale > 1.05f) ? "LARGE" : "NORMAL"));
			ui.SetText(values[TabletAction.RollLock], settings.RollLock.Value ? "ON" : "OFF");
			ui.RefreshLayout();
		}
	}

	private static TabletAction ModeAction(CameraMode mode)
	{
		return mode switch
		{
			CameraMode.FirstPerson => TabletAction.FirstPerson, 
			CameraMode.Selfie => TabletAction.ThirdPerson, 
			CameraMode.GoPro => TabletAction.GoPro, 
			CameraMode.Follow => TabletAction.Follow, 
			CameraMode.Track => TabletAction.Track, 
			CameraMode.Tripod => TabletAction.Lock, 
			_ => TabletAction.None, 
		};
	}

	private void DrawIcon(TabletAction action, Vector2 center, float z, float scale, Transform parent)
	{
		Action<float, float, float, float> line = delegate(float x1, float y1, float x2, float y2)
		{
			Line(center + new Vector2(x1, y1) * scale, center + new Vector2(x2, y2) * scale, z, 0.0016f * scale, parent);
		};
		Action<float, float, float> action2 = delegate(float x, float y, float r)
		{
			Arc(center + new Vector2(x, y) * scale, r * scale, 0f, 360f, z, 0.0016f * scale, parent);
		};
		Action<float, float, float, float> action3 = delegate(float x, float y, float w, float h)
		{
			line(x - w / 2f, y - h / 2f, x + w / 2f, y - h / 2f);
			line(x + w / 2f, y - h / 2f, x + w / 2f, y + h / 2f);
			line(x + w / 2f, y + h / 2f, x - w / 2f, y + h / 2f);
			line(x - w / 2f, y + h / 2f, x - w / 2f, y - h / 2f);
		};
		switch (action)
		{
		case TabletAction.FirstPerson:
			line(-0.018f, 0f, -0.01f, 0.008f);
			line(-0.01f, 0.008f, 0f, 0.011f);
			line(0f, 0.011f, 0.01f, 0.008f);
			line(0.01f, 0.008f, 0.018f, 0f);
			line(-0.018f, 0f, -0.01f, -0.008f);
			line(-0.01f, -0.008f, 0f, -0.011f);
			line(0f, -0.011f, 0.01f, -0.008f);
			line(0.01f, -0.008f, 0.018f, 0f);
			action2(0f, 0f, 0.005f);
			break;
		case TabletAction.ThirdPerson:
			action2(0f, 0.008f, 0.0045f);
			line(-0.009f, -0.014f, -0.009f, -0.003f);
			line(-0.009f, -0.003f, 0.009f, -0.003f);
			line(0.009f, -0.003f, 0.009f, -0.014f);
			Arc(center, 0.02f * scale, 140f, 220f, z, 0.0016f * scale, parent);
			Arc(center, 0.02f * scale, -40f, 40f, z, 0.0016f * scale, parent);
			break;
		case TabletAction.GoPro:
			action3(0f, 0f, 0.035f, 0.026f);
			action3(-0.008f, 0.001f, 0.009f, 0.01f);
			action2(0.007f, 0.001f, 0.006f);
			line(0f, -0.013f, 0f, -0.019f);
			line(-0.009f, -0.019f, 0.009f, -0.019f);
			break;
		case TabletAction.Follow:
			action2(0.01f, 0.007f, 0.0045f);
			line(0.003f, -0.014f, 0.003f, -0.003f);
			line(0.003f, -0.003f, 0.017f, -0.003f);
			line(0.017f, -0.003f, 0.017f, -0.014f);
			line(-0.018f, -0.01f, -0.018f, 0.009f);
			line(-0.018f, 0.009f, -0.007f, 0.009f);
			line(-0.007f, 0.009f, -0.012f, 0.014f);
			line(-0.007f, 0.009f, -0.012f, 0.004f);
			break;
		case TabletAction.Track:
		{
			action2(0f, 0.005f, 0.0045f);
			line(-0.007f, -0.01f, -0.007f, -0.004f);
			line(-0.007f, -0.004f, 0.007f, -0.004f);
			line(0.007f, -0.004f, 0.007f, -0.01f);
			float[] array = new float[2] { -1f, 1f };
			foreach (float num3 in array)
			{
				float[] array2 = new float[2] { -1f, 1f };
				foreach (float num5 in array2)
				{
					line(num3 * 0.02f, num5 * 0.009f, num3 * 0.02f, num5 * 0.016f);
					line(num3 * 0.02f, num5 * 0.016f, num3 * 0.012f, num5 * 0.016f);
				}
			}
			break;
		}
		case TabletAction.Lock:
			action3(0f, -0.004f, 0.026f, 0.022f);
			Arc(center + new Vector2(0f, 0.008f) * scale, 0.008f * scale, 0f, 180f, z, 0.0017f * scale, parent);
			action2(0f, -0.002f, 0.002f);
			line(0f, -0.004f, 0f, -0.009f);
			break;
		case TabletAction.Flip:
			Arc(center, 0.014f * scale, 20f, 160f, z, 0.0022f * scale, parent);
			Arc(center, 0.014f * scale, 200f, 340f, z, 0.0022f * scale, parent);
			line(-0.013f, 0.005f, -0.015f, 0.014f);
			line(-0.013f, 0.005f, -0.005f, 0.008f);
			line(0.013f, -0.005f, 0.015f, -0.014f);
			line(0.013f, -0.005f, 0.005f, -0.008f);
			break;
		case TabletAction.Clip:
			action3(-0.004f, 0f, 0.024f, 0.022f);
			line(0.008f, 0.005f, 0.018f, 0.011f);
			line(0.018f, 0.011f, 0.018f, -0.011f);
			line(0.018f, -0.011f, 0.008f, -0.005f);
			break;
		case TabletAction.Photo:
			action3(0f, -0.002f, 0.031f, 0.022f);
			action2(0f, -0.002f, 0.007f);
			action3(0f, 0.012f, 0.014f, 0.005f);
			break;
		case TabletAction.Desktop:
			action3(0f, 0.002f, 0.033f, 0.022f);
			line(0f, -0.009f, 0f, -0.017f);
			line(-0.011f, -0.017f, 0.011f, -0.017f);
			break;
		case TabletAction.Settings:
		{
			action2(0f, 0f, 0.01f);
			action2(0f, 0f, 0.004f);
			for (int num = 0; num < 8; num++)
			{
				float f = (float)num * MathF.PI / 4f;
				line(Mathf.Cos(f) * 0.011f, Mathf.Sin(f) * 0.011f, Mathf.Cos(f) * 0.016f, Mathf.Sin(f) * 0.016f);
			}
			break;
		}
		}
	}

	private void Line(Vector2 a, Vector2 b, float z, float width, Transform parent)
	{
		Vector2 vector = b - a;
		float magnitude = vector.magnitude;
		if (!(magnitude < 1E-06f))
		{
			Vector2 vector2 = new Vector2(0f - vector.y, vector.x) * (width / (2f * magnitude));
			int count = iconVertices.Count;
			iconVertices.Add(new Vector3(a.x - vector2.x, a.y - vector2.y, z));
			iconVertices.Add(new Vector3(b.x - vector2.x, b.y - vector2.y, z));
			iconVertices.Add(new Vector3(b.x + vector2.x, b.y + vector2.y, z));
			iconVertices.Add(new Vector3(a.x + vector2.x, a.y + vector2.y, z));
			iconTriangles.Add(count);
			iconTriangles.Add(count + 2);
			iconTriangles.Add(count + 1);
			iconTriangles.Add(count);
			iconTriangles.Add(count + 3);
			iconTriangles.Add(count + 2);
		}
	}

	private void Arc(Vector2 center, float radius, float start, float end, float z, float width, Transform parent)
	{
		int num = Mathf.Max(3, Mathf.CeilToInt(Mathf.Abs(end - start) / 30f));
		Vector2 a = center + new Vector2(Mathf.Cos(start * (MathF.PI / 180f)), Mathf.Sin(start * (MathF.PI / 180f))) * radius;
		for (int i = 1; i <= num; i++)
		{
			float f = Mathf.Lerp(start, end, (float)i / (float)num) * (MathF.PI / 180f);
			Vector2 vector = center + new Vector2(Mathf.Cos(f), Mathf.Sin(f)) * radius;
			Line(a, vector, z, width, parent);
			a = vector;
		}
	}

	internal static void Bind(Renderer renderer, Texture texture, bool mirror, Vector2 originalSize)
	{
		if (renderer == null)
		{
			return;
		}
		renderer.enabled = texture != null;
		if (!(texture == null))
		{
			Material sharedMaterial = renderer.sharedMaterial;
			if (sharedMaterial.HasProperty("_BaseMap"))
			{
				sharedMaterial.SetTexture("_BaseMap", texture);
				sharedMaterial.SetTextureScale("_BaseMap", new Vector2((!mirror) ? 1 : (-1), 1f));
				sharedMaterial.SetTextureOffset("_BaseMap", new Vector2(mirror ? 1 : 0, 0f));
			}
			if (sharedMaterial.HasProperty("_MainTex"))
			{
				sharedMaterial.SetTexture("_MainTex", texture);
				sharedMaterial.SetTextureScale("_MainTex", new Vector2((!mirror) ? 1 : (-1), 1f));
				sharedMaterial.SetTextureOffset("_MainTex", new Vector2(mirror ? 1 : 0, 0f));
			}
			FrameRect frameRect = CameraMath.FitFrame(originalSize.x, originalSize.y, texture.width, texture.height);
			renderer.transform.localScale = new Vector3(frameRect.Width / originalSize.x, frameRect.Height / originalSize.y, 1f);
		}
	}

	public void Dispose()
	{
		ui.Dispose();
	}
}
