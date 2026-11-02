using System;
using System.Globalization;
using System.Text;
using BepInEx.Logging;
using GorillaLocomotion;
using Pigless.Visuals;
using UnityEngine;

namespace Pigless.Sheesh;

internal sealed class VisualTools : IDisposable
{
	private const int DotsPerHand = 20;

	private readonly ManualLogSource logger;

	private readonly WorldUi world;

	private readonly Transform[] dots = new Transform[40];

	private readonly float[] born = new float[40];

	private readonly Transform[] markers = new Transform[2];

	private readonly Light[] lights = new Light[2];

	private WorldUi hud;

	private Component hudText;

	private GTPlayer previousPlayer;

	private bool trailsBuilt;

	private bool markersBuilt;

	private bool lightsBuilt;

	private bool disposed;

	private int nextDot;

	private float nextSample;

	private float nextHudUpdate;

	private float startedAt;

	private float fpsTime;

	private float fps;

	private int fpsFrames;

	internal bool Trails;

	internal bool HandLights;

	internal bool Markers;

	internal bool ShowFps;

	internal bool ShowSpeed;

	internal bool ShowTimer;

	internal bool ShowCoordinates;

	internal int TrailPreset;

	internal VisualTools(ManualLogSource logger)
	{
		this.logger = logger ?? throw new ArgumentNullException("logger");
		world = new WorldUi("Pigless Sheesh / local visual tools", logger);
		world.Root.gameObject.layer = 5;
		UnityEngine.Object.DontDestroyOnLoad(world.Root.gameObject);
		startedAt = Time.unscaledTime;
	}

	internal void Tick(GTPlayer player, Transform left, Transform right, float delta)
	{
		if (disposed)
		{
			return;
		}
		float unscaledTime = Time.unscaledTime;
		if (delta > 0f && !float.IsNaN(delta) && !float.IsInfinity(delta))
		{
			fpsTime += delta;
			fpsFrames++;
			if (fpsTime >= 0.25f)
			{
				fps = (float)fpsFrames / fpsTime;
				fpsFrames = 0;
				fpsTime = 0f;
			}
		}
		if ((UnityEngine.Object)(object)player == null || player.mainCamera == null || left == null || right == null)
		{
			HideAll();
			previousPlayer = null;
			return;
		}
		if ((UnityEngine.Object)(object)previousPlayer != (UnityEngine.Object)(object)player)
		{
			ClearDots();
			nextSample = unscaledTime;
			previousPlayer = player;
		}
		if (Trails)
		{
			try
			{
				TickTrails(left, right, unscaledTime);
			}
			catch (Exception ex)
			{
				Trails = false;
				ClearDots();
				logger.LogError("Hand trails disabled after a visual error: " + ex);
			}
		}
		else
		{
			ClearDots();
		}
		if (Markers)
		{
			try
			{
				EnsureMarkers();
				MoveMarker(markers[0], left);
				MoveMarker(markers[1], right);
			}
			catch (Exception ex2)
			{
				Markers = false;
				HideMarkers();
				logger.LogError("Controller markers disabled after a visual error: " + ex2);
			}
		}
		else
		{
			HideMarkers();
		}
		if (HandLights)
		{
			try
			{
				EnsureLights();
				TickLight(lights[0], left);
				TickLight(lights[1], right);
			}
			catch (Exception ex3)
			{
				HandLights = false;
				HideLights();
				logger.LogError("Hand lights disabled after a visual error: " + ex3);
			}
		}
		else
		{
			HideLights();
		}
		if (ShowFps || ShowSpeed || ShowTimer || ShowCoordinates)
		{
			try
			{
				TickHud(player, left, unscaledTime);
				return;
			}
			catch (Exception ex4)
			{
				ShowFps = (ShowSpeed = (ShowTimer = (ShowCoordinates = false)));
				hud?.Dispose();
				hud = null;
				hudText = null;
				logger.LogError("Wrist stats disabled after a text error: " + ex4);
				return;
			}
		}
		if (hud != null)
		{
			hud.Root.gameObject.SetActive(value: false);
		}
	}

	private void EnsureTrails()
	{
		if (trailsBuilt)
		{
			return;
		}
		for (int i = 0; i < dots.Length; i++)
		{
			if (!(dots[i] != null))
			{
				dots[i] = world.Primitive("Trail dot " + i, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.012f, (i < 20) ? Theme.Rim : Theme.White).transform;
				dots[i].gameObject.SetActive(value: false);
				born[i] = float.NegativeInfinity;
			}
		}
		trailsBuilt = true;
	}

	private void TickTrails(Transform left, Transform right, float now)
	{
		EnsureTrails();
		float num = Mathf.Clamp(TrailPreset, 0, 2) switch
		{
			1 => 0.9f, 
			0 => 0.45f, 
			_ => 1.5f, 
		};
		if (now >= nextSample)
		{
			int num2 = nextDot;
			int num3 = nextDot + 20;
			dots[num2].position = left.position;
			dots[num3].position = right.position;
			born[num2] = (born[num3] = now);
			dots[num2].gameObject.SetActive(value: true);
			dots[num3].gameObject.SetActive(value: true);
			nextDot = (nextDot + 1) % 20;
			nextSample = now + num / 20f;
		}
		for (int i = 0; i < dots.Length; i++)
		{
			if (now - born[i] >= num)
			{
				dots[i].gameObject.SetActive(value: false);
				continue;
			}
			float num4 = Mathf.Clamp01(1f - (now - born[i]) / num);
			dots[i].localScale = Vector3.one * (0.003f + 0.01f * num4);
		}
	}

	private void EnsureMarkers()
	{
		if (markersBuilt)
		{
			return;
		}
		for (int i = 0; i < 2; i++)
		{
			if (markers[i] != null)
			{
				continue;
			}
			Transform transform = world.Group((i == 0) ? "Left controller marker" : "Right controller marker");
			markers[i] = transform;
			for (int j = -1; j <= 1; j += 2)
			{
				for (int k = -1; k <= 1; k += 2)
				{
					world.Primitive("Corner horizontal", PrimitiveType.Cube, new Vector3((float)j * 0.021f, (float)k * 0.026f, 0.005f), new Vector3(0.015f, 0.002f, 0.002f), Theme.White, collision: false, transform);
					world.Primitive("Corner vertical", PrimitiveType.Cube, new Vector3((float)j * 0.026f, (float)k * 0.021f, 0.005f), new Vector3(0.002f, 0.015f, 0.002f), Theme.Rim, collision: false, transform);
				}
			}
			transform.gameObject.SetActive(value: false);
		}
		markersBuilt = true;
	}

	private static void MoveMarker(Transform marker, Transform hand)
	{
		marker.SetPositionAndRotation(hand.position, hand.rotation);
		marker.gameObject.SetActive(value: true);
	}

	private void EnsureLights()
	{
		if (lightsBuilt)
		{
			return;
		}
		for (int i = 0; i < 2; i++)
		{
			if (!(lights[i] != null))
			{
				GameObject gameObject = new GameObject((i == 0) ? "Pigless left hand light" : "Pigless right hand light");
				gameObject.layer = world.Root.gameObject.layer;
				gameObject.transform.SetParent(world.Root, worldPositionStays: false);
				Light light = gameObject.AddComponent<Light>();
				lights[i] = light;
				light.type = LightType.Point;
				light.color = Theme.White;
				light.range = 4f;
				light.intensity = 0.8f;
				light.shadows = LightShadows.None;
				light.enabled = false;
			}
		}
		lightsBuilt = true;
	}

	private static void TickLight(Light light, Transform hand)
	{
		light.transform.position = hand.position + hand.forward * 0.04f;
		light.enabled = true;
	}

	private void TickHud(GTPlayer player, Transform left, float now)
	{
		if (hud == null)
		{
			hud = new WorldUi("Pigless Sheesh / wrist stats", logger);
			hud.Root.gameObject.layer = 5;
			UnityEngine.Object.DontDestroyOnLoad(hud.Root.gameObject);
			hud.Panel("Stats rim", new Vector2(0.252f, 0.152f), 0.018f, 0.009f, Vector3.zero, Theme.Rim);
			hud.Panel("Stats face", new Vector2(0.244f, 0.144f), 0.015f, 0.006f, new Vector3(0f, 0f, -0.006f), Theme.Brown);
			hud.Label("PIGLESS / LIVE STATS", new Vector3(0f, 0.052f, -0.011f), 0.011f, Theme.White, "MiddleCenter", 0.22f);
			hudText = hud.Label("", new Vector3(-0.106f, -0.01f, -0.012f), 0.088f, Theme.White, "MiddleLeft", 0.212f);
			nextHudUpdate = 0f;
		}
		Transform transform = player.mainCamera.transform;
		Vector3 vector = left.position - transform.right * 0.12f + Vector3.up * 0.07f + transform.forward * 0.07f;
		hud.Root.SetPositionAndRotation(vector, Quaternion.LookRotation(vector - transform.position, transform.up));
		hud.Root.gameObject.SetActive(value: true);
		if (now >= nextHudUpdate)
		{
			nextHudUpdate = now + 0.2f;
			StringBuilder stringBuilder = new StringBuilder();
			if (ShowFps)
			{
				Append(stringBuilder, "FPS  " + Mathf.RoundToInt(fps));
			}
			if (ShowSpeed)
			{
				Append(stringBuilder, "SPEED  " + ((player.playerRigidBody != null) ? player.playerRigidBody.velocity.magnitude : 0f).ToString("0.0", CultureInfo.InvariantCulture) + " m/s");
			}
			if (ShowTimer)
			{
				int num = Mathf.Max(0, Mathf.FloorToInt(now - startedAt));
				Append(stringBuilder, "TIME  " + (num / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (num % 60).ToString("00", CultureInfo.InvariantCulture));
			}
			if (ShowCoordinates)
			{
				Vector3 position = ((Component)(object)player).transform.position;
				Append(stringBuilder, string.Format(CultureInfo.InvariantCulture, "POS  {0:0.0}, {1:0.0}, {2:0.0}", position.x, position.y, position.z));
			}
			hud.SetText(hudText, stringBuilder.ToString());
		}
		hud.RefreshLayout();
	}

	private static void Append(StringBuilder text, string line)
	{
		if (text.Length > 0)
		{
			text.Append('\n');
		}
		text.Append(line);
	}

	private void ClearDots()
	{
		for (int i = 0; i < dots.Length; i++)
		{
			born[i] = float.NegativeInfinity;
			if (dots[i] != null)
			{
				dots[i].gameObject.SetActive(value: false);
			}
		}
		nextDot = 0;
		nextSample = Time.unscaledTime;
	}

	private void HideMarkers()
	{
		Transform[] array = markers;
		foreach (Transform transform in array)
		{
			if (transform != null)
			{
				transform.gameObject.SetActive(value: false);
			}
		}
	}

	private void HideLights()
	{
		Light[] array = lights;
		foreach (Light light in array)
		{
			if (light != null)
			{
				light.enabled = false;
			}
		}
	}

	private void HideAll()
	{
		ClearDots();
		HideMarkers();
		HideLights();
		if (hud != null)
		{
			hud.Root.gameObject.SetActive(value: false);
		}
	}

	internal void Reset()
	{
		Trails = (HandLights = (Markers = (ShowFps = (ShowSpeed = (ShowTimer = (ShowCoordinates = false))))));
		TrailPreset = 0;
		startedAt = Time.unscaledTime;
		fpsFrames = 0;
		fpsTime = (fps = 0f);
		if (!disposed)
		{
			HideAll();
		}
	}

	public void Dispose()
	{
		if (!disposed)
		{
			Reset();
			disposed = true;
			hud?.Dispose();
			hud = null;
			world.Dispose();
		}
	}
}
