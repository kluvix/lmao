using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using GorillaLocomotion;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pigless.CameraMod;

internal sealed class CameraRig : IDisposable
{
	private struct Shot
	{
		internal Vector3 Position;

		internal Quaternion Rotation;
	}

	private readonly CameraSettings settings;

	private readonly ManualLogSource log;

	private Camera camera;

	private Camera source;

	private GTPlayer player;

	private RenderTexture texture;

	private DesktopPresenter desktop;

	private Shot a;

	private Shot b;

	private Shot tripod;

	private Shot track;

	private CameraMode previousMode;

	private CameraMode statusMode;

	private bool hasA;

	private bool hasB;

	private bool tripodSet;

	private bool trackSet;

	private bool hasPose;

	private bool modeKnown;

	private bool dollyPlaying;

	private bool disposed;

	private bool presenterFailed;

	private float orbitAngle;

	private float dollyStarted;

	private float nextError;

	private int mirrorLayer = -1;

	private int firstPersonLayer = -1;

	private int statusWidth;

	private int statusHeight;

	private string outputDescription;

	private Vector3 headHeading = Vector3.forward;

	internal Camera SceneCamera => camera;

	internal RenderTexture OutputTexture => texture;

	internal string Status { get; private set; } = "CAMERA OFF";

	internal string LastPhoto { get; private set; } = "";

	internal bool HasA => hasA;

	internal bool HasB => hasB;

	internal string DesktopStatus
	{
		get
		{
			if (settings.DesktopOutput.Value && settings.Enabled.Value && !(camera == null) && camera.enabled)
			{
				if (!presenterFailed)
				{
					if (desktop == null)
					{
						return "DESKTOP STARTING";
					}
					return desktop.State;
				}
				return "DESKTOP ERROR - TOGGLE DESKTOP";
			}
			return "DESKTOP OFF";
		}
	}

	internal CameraRig(CameraSettings settings, ManualLogSource log)
	{
		this.settings = settings;
		this.log = log;
		Camera.onPreCull = (Camera.CameraCallback)Delegate.Combine(Camera.onPreCull, new Camera.CameraCallback(OnPreCull));
		RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
	}

	private static float Safe(float value, float fallback, float min, float max)
	{
		if (!float.IsNaN(value) && !float.IsInfinity(value))
		{
			return Mathf.Clamp(value, min, max);
		}
		return fallback;
	}

	private void OnBeginCameraRendering(ScriptableRenderContext context, Camera rendered)
	{
		SyncHeadPose(rendered);
	}

	private void OnPreCull(Camera rendered)
	{
		SyncHeadPose(rendered);
	}

	private void SyncHeadPose(Camera rendered)
	{
		if (!disposed && !(camera == null) && !(rendered != camera) && !(source == null) && settings.Enabled.Value && IsHeadView(settings.Mode.Value) && !settings.Flipped.Value)
		{
			Transform transform = source.transform;
			Quaternion rotation = CameraOrientation.ApplyRollLock(transform.rotation, headHeading, settings.RollLock.Value);
			camera.transform.SetPositionAndRotation(transform.position, rotation);
		}
	}

	internal void Tick(GTPlayer current, Transform hand, float delta)
	{
		if (disposed)
		{
			return;
		}
		if (!settings.Enabled.Value || current == null || current.mainCamera == null)
		{
			if (camera != null)
			{
				camera.enabled = false;
			}
			hasPose = false;
			Status = (settings.Enabled.Value ? "WAITING FOR PLAYER" : "CAMERA OFF");
			return;
		}
		try
		{
			if (current != player)
			{
				player = current;
				source = null;
				hasPose = (tripodSet = (trackSet = (hasA = (hasB = (modeKnown = (dollyPlaying = false))))));
			}
			EnsureCamera(current.mainCamera);
			EnsureTexture();
			CameraMode cameraMode = settings.Mode.Value;
			if ((uint)cameraMode > 8u)
			{
				cameraMode = CameraMode.Follow;
				settings.Mode.Value = cameraMode;
			}
			if (!modeKnown || previousMode != cameraMode)
			{
				if (cameraMode == CameraMode.Track)
				{
					track = CurrentOrFallback(current.mainCamera.transform);
					trackSet = true;
				}
				if (cameraMode == CameraMode.Tripod && !tripodSet)
				{
					tripod = (hasPose ? CaptureStoredPose() : CurrentOrFallback(current.mainCamera.transform));
					tripodSet = true;
				}
				previousMode = cameraMode;
				modeKnown = true;
				hasPose = false;
			}
			camera.fieldOfView = Safe(settings.Fov.Value, 70f, 30f, 110f);
			camera.nearClipPlane = 0.03f;
			camera.aspect = (float)texture.width / (float)texture.height;
			int cullingMask = source.cullingMask;
			int num = 2;
			int num2 = 5;
			cullingMask &= ~(1 << num);
			if (!settings.ShowInterface.Value)
			{
				cullingMask &= ~(1 << num2);
			}
			if (!IsHeadView(cameraMode) || settings.Flipped.Value)
			{
				if (mirrorLayer >= 0)
				{
					cullingMask |= 1 << mirrorLayer;
				}
				if (firstPersonLayer >= 0)
				{
					cullingMask &= ~(1 << firstPersonLayer);
				}
			}
			camera.cullingMask = cullingMask;
			Shot shot = Target(current.mainCamera.transform, hand, Safe(delta, 0.014f, 0f, 0.25f));
			bool flag = cameraMode == CameraMode.Dolly || IsHeadView(cameraMode) || cameraMode == CameraMode.Tripod;
			float t = ((!hasPose || flag) ? 1f : CameraMath.Damping(Safe(settings.Smoothing.Value, 10f, 0f, 30f), delta));
			Vector3 vector = (hasPose ? Vector3.Lerp(camera.transform.position, shot.Position, t) : shot.Position);
			if (settings.CollisionAvoidance.Value && HasWallGuard(cameraMode))
			{
				vector = Avoid(current.mainCamera.transform.position, vector);
			}
			if (IsAutomaticView(cameraMode) || (IsHeadView(cameraMode) && settings.Flipped.Value))
			{
				Vector3 vector2 = current.mainCamera.transform.position + (IsHeadView(cameraMode) ? Vector3.zero : (Vector3.down * 0.15f));
				shot.Rotation = CameraOrientation.Aim(vector2 - vector, current.mainCamera.transform.up, camera.transform.forward, settings.RollLock.Value);
				if (cameraMode == CameraMode.Track && settings.Flipped.Value)
				{
					shot.Rotation = CameraOrientation.Reverse(shot.Rotation);
				}
			}
			Quaternion rotation = (hasPose ? Quaternion.Slerp(camera.transform.rotation, shot.Rotation, t) : shot.Rotation);
			rotation = CameraOrientation.ApplyRollLock(rotation, shot.Rotation * Vector3.forward, settings.RollLock.Value);
			camera.transform.SetPositionAndRotation(vector, rotation);
			hasPose = true;
			camera.enabled = true;
			if (outputDescription == null || statusMode != settings.Mode.Value || statusWidth != texture.width || statusHeight != texture.height)
			{
				statusMode = settings.Mode.Value;
				statusWidth = texture.width;
				statusHeight = texture.height;
				outputDescription = ModeLabel(statusMode) + " / " + statusWidth + "x" + statusHeight;
			}
			Status = outputDescription;
		}
		catch (Exception ex)
		{
			if (camera != null)
			{
				camera.enabled = false;
			}
			Status = "CAMERA ERROR - CHECK LOG";
			if (Time.unscaledTime >= nextError)
			{
				nextError = Time.unscaledTime + 5f;
				log.LogError("Camera rendering setup failed: " + ex);
			}
		}
	}

	private void EnsureCamera(Camera head)
	{
		if (camera == null)
		{
			GameObject gameObject = new GameObject("Pigless Camera / filming camera");
			UnityEngine.Object.DontDestroyOnLoad(gameObject);
			camera = gameObject.AddComponent<Camera>();
			camera.enabled = false;
		}
		if (source == head)
		{
			return;
		}
		RetryDesktop();
		camera.CopyFrom(head);
		camera.enabled = false;
		camera.tag = "Untagged";
		camera.stereoTargetEye = StereoTargetEyeMask.None;
		camera.targetTexture = null;
		camera.allowMSAA = false;
		camera.allowHDR = false;
		if (camera.clearFlags == CameraClearFlags.Depth || camera.clearFlags == CameraClearFlags.Nothing)
		{
			camera.clearFlags = CameraClearFlags.Skybox;
		}
		camera.ResetProjectionMatrix();
		camera.ResetWorldToCameraMatrix();
		camera.ResetCullingMatrix();
		Type type = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime", throwOnError: false);
		if (type != null)
		{
			Component component = camera.GetComponent(type) ?? camera.gameObject.AddComponent(type);
			Set(component, "allowXRRendering", false);
			Set(component, "renderPostProcessing", false);
			PropertyInfo property = type.GetProperty("renderType");
			if (property != null && property.CanWrite)
			{
				property.SetValue(component, Enum.Parse(property.PropertyType, "Base"), null);
			}
		}
		mirrorLayer = LayerMask.NameToLayer("MirrorOnly");
		firstPersonLayer = LayerMask.NameToLayer("FirstPersonOnly");
		source = head;
		hasPose = false;
	}

	private static void Set(Component component, string property, object value)
	{
		PropertyInfo property2 = component.GetType().GetProperty(property);
		if (property2 != null && property2.CanWrite)
		{
			property2.SetValue(component, value, null);
		}
	}

	private void EnsureTexture()
	{
		CameraMath.GetResolution(settings.ResolutionPreset.Value, settings.AspectPreset.Value, Mathf.Max(1, SystemInfo.maxTextureSize), out var width, out var height);
		if (texture != null && texture.width == width && texture.height == height && texture.IsCreated())
		{
			if (camera.targetTexture != texture)
			{
				camera.targetTexture = texture;
			}
			return;
		}
		if (camera != null)
		{
			camera.targetTexture = null;
		}
		if (texture != null)
		{
			texture.Release();
			UnityEngine.Object.Destroy(texture);
			texture = null;
		}
		RenderTexture renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
		{
			name = "Pigless Camera / live output",
			antiAliasing = 1,
			filterMode = FilterMode.Bilinear,
			useMipMap = false,
			autoGenerateMips = false
		};
		if (!renderTexture.Create())
		{
			UnityEngine.Object.Destroy(renderTexture);
			throw new InvalidOperationException("The selected camera render size is unsupported.");
		}
		texture = renderTexture;
		camera.targetTexture = texture;
		log.LogInfo("Camera output created: " + width + "x" + height + "; mono render texture, headset untouched.");
	}

	private Shot Target(Transform head, Transform hand, float delta)
	{
		CameraMode value = settings.Mode.Value;
		float num = Safe(settings.Distance.Value, 2.2f, 0.6f, 6f);
		float num2 = Safe(settings.Height.Value, 0.25f, -1f, 3f);
		float num3 = Safe(settings.Shoulder.Value, 0.35f, -0.8f, 0.8f);
		Vector3 vector = (headHeading = CameraOrientation.Heading(head.forward, headHeading));
		Vector3 normalized = Vector3.Cross(Vector3.up, vector).normalized;
		Vector3 vector2 = head.position + Vector3.down * 0.15f;
		Shot result = default(Shot);
		switch (value)
		{
		case CameraMode.FirstPerson:
		case CameraMode.GoPro:
			result = new Shot
			{
				Position = CameraOrientation.HeadPosition(head.position, vector, settings.Flipped.Value),
				Rotation = head.rotation
			};
			if (settings.Flipped.Value)
			{
				result.Rotation = CameraOrientation.Aim(head.position - result.Position, head.up, head.forward, settings.RollLock.Value);
			}
			break;
		case CameraMode.Selfie:
			result.Position = head.position + vector * (settings.Flipped.Value ? (0f - num) : num) + normalized * num3 + Vector3.up * num2;
			result.Rotation = Look(vector2 - result.Position, head.up);
			break;
		case CameraMode.Orbit:
			orbitAngle = (orbitAngle + Safe(settings.OrbitSpeed.Value, 20f, -90f, 90f) * delta) % 360f;
			result.Position = vector2 + Quaternion.Euler(0f, orbitAngle + (float)(settings.Flipped.Value ? 180 : 0), 0f) * (-vector * num) + Vector3.up * num2;
			result.Rotation = Look(vector2 - result.Position, head.up);
			break;
		case CameraMode.Tripod:
			if (!tripodSet)
			{
				tripod = CurrentOrFallback(head);
				tripodSet = true;
			}
			result = tripod;
			break;
		case CameraMode.Track:
			if (!trackSet)
			{
				track = CurrentOrFallback(head);
				trackSet = true;
			}
			result = new Shot
			{
				Position = track.Position,
				Rotation = Look(vector2 - track.Position, head.up)
			};
			break;
		case CameraMode.Handheld:
			if (hand == null)
			{
				throw new InvalidOperationException("Filming camera lens pose is unavailable.");
			}
			result = new Shot
			{
				Position = hand.position + hand.forward * 0.02f,
				Rotation = hand.rotation
			};
			break;
		case CameraMode.Dolly:
		{
			if (!hasA || !hasB)
			{
				result = CurrentOrFallback(head);
				break;
			}
			if (!dollyPlaying)
			{
				result = a;
				break;
			}
			bool complete;
			float t = CameraMath.DollyProgress(Time.unscaledTime - dollyStarted, Safe(settings.DollyDuration.Value, 8f, 1f, 60f), settings.DollyLoop.Value, out complete);
			result = new Shot
			{
				Position = Vector3.Lerp(a.Position, b.Position, t),
				Rotation = Quaternion.Slerp(a.Rotation, b.Rotation, t)
			};
			if (complete)
			{
				dollyPlaying = false;
				tripod = result;
				tripodSet = true;
				settings.Mode.Value = CameraMode.Tripod;
			}
			break;
		}
		default:
			result.Position = head.position + vector * (settings.Flipped.Value ? num : (0f - num)) + normalized * num3 + Vector3.up * num2;
			result.Rotation = Look(vector2 - result.Position, head.up);
			break;
		}
		if (settings.CollisionAvoidance.Value && HasWallGuard(value))
		{
			result.Position = Avoid(head.position, result.Position);
		}
		if (settings.Flipped.Value && (value == CameraMode.Handheld || value == CameraMode.Tripod || value == CameraMode.Dolly))
		{
			result.Rotation = CameraOrientation.Reverse(result.Rotation);
		}
		return result;
	}

	private Quaternion Look(Vector3 forward, Vector3 suppliedUp)
	{
		return CameraOrientation.Aim(forward, suppliedUp, headHeading, settings.RollLock.Value);
	}

	private static bool IsHeadView(CameraMode mode)
	{
		if (mode != CameraMode.FirstPerson)
		{
			return mode == CameraMode.GoPro;
		}
		return true;
	}

	private static bool IsFollowing(CameraMode mode)
	{
		if (mode != CameraMode.Follow && mode != CameraMode.Selfie)
		{
			return mode == CameraMode.Orbit;
		}
		return true;
	}

	private static bool IsAutomaticView(CameraMode mode)
	{
		if (!IsFollowing(mode))
		{
			return mode == CameraMode.Track;
		}
		return true;
	}

	private bool HasWallGuard(CameraMode mode)
	{
		if (!IsFollowing(mode))
		{
			if (IsHeadView(mode))
			{
				return settings.Flipped.Value;
			}
			return false;
		}
		return true;
	}

	private static Vector3 Avoid(Vector3 origin, Vector3 desired)
	{
		Vector3 vector = desired - origin;
		float magnitude = vector.magnitude;
		if (magnitude < 0.02f)
		{
			return desired;
		}
		if (Physics.SphereCast(origin, 0.1f, vector / magnitude, out var hitInfo, magnitude, GTPlayer.LocomotionEnabledLayers, QueryTriggerInteraction.Ignore))
		{
			return origin + vector / magnitude * Mathf.Max(0.05f, hitInfo.distance - 0.04f);
		}
		return desired;
	}

	private Shot CurrentOrFallback(Transform head)
	{
		if (!hasPose || !(camera != null))
		{
			return new Shot
			{
				Position = head.position - head.forward * 2f + Vector3.up * 0.3f,
				Rotation = Look(head.forward, head.up)
			};
		}
		return new Shot
		{
			Position = camera.transform.position,
			Rotation = camera.transform.rotation
		};
	}

	internal static string ModeLabel(CameraMode mode)
	{
		return mode switch
		{
			CameraMode.FirstPerson => "1ST PERSON", 
			CameraMode.Selfie => "3RD PERSON", 
			CameraMode.GoPro => "GO PRO", 
			CameraMode.Handheld => "FREE CAMERA", 
			CameraMode.Track => "TRACK MONKE", 
			CameraMode.Tripod => "CAMERA LOCKED", 
			_ => mode.ToString().ToUpperInvariant(), 
		};
	}

	internal void SetMode(CameraMode mode)
	{
		if ((uint)mode > 8u)
		{
			mode = CameraMode.Follow;
		}
		settings.Enabled.Value = true;
		if (mode == CameraMode.Tripod)
		{
			tripodSet = false;
			if (camera != null && hasPose)
			{
				PlaceTripod();
				return;
			}
		}
		if (mode == CameraMode.Track)
		{
			trackSet = false;
			if (camera != null && hasPose)
			{
				track = new Shot
				{
					Position = camera.transform.position,
					Rotation = camera.transform.rotation
				};
				trackSet = true;
			}
		}
		settings.Mode.Value = mode;
		previousMode = mode;
		modeKnown = true;
		if (mode != CameraMode.Dolly)
		{
			dollyPlaying = false;
		}
		if (mode == CameraMode.Orbit)
		{
			orbitAngle = 0f;
		}
		hasPose = false;
		Status = ModeLabel(mode);
	}

	internal void SetFlipped(bool value)
	{
		if (settings.Flipped.Value != value)
		{
			settings.Flipped.Value = value;
			hasPose = false;
			outputDescription = null;
			Status = (value ? "CAMERA FLIPPED" : "CAMERA FORWARD");
		}
	}

	internal void ToggleFlip()
	{
		SetFlipped(!settings.Flipped.Value);
	}

	private Shot CaptureStoredPose()
	{
		Shot result = new Shot
		{
			Position = camera.transform.position,
			Rotation = camera.transform.rotation
		};
		if (settings.Flipped.Value)
		{
			result.Rotation = CameraOrientation.Reverse(result.Rotation);
		}
		return result;
	}

	internal void Recenter()
	{
		GTPlayer instance = GTPlayer.Instance;
		if (instance == null || instance.mainCamera == null)
		{
			Status = "WAITING FOR PLAYER";
			return;
		}
		Transform transform = instance.mainCamera.transform;
		Vector3 vector = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
		if (vector.sqrMagnitude < 0.0001f)
		{
			vector = Vector3.forward;
		}
		else
		{
			vector.Normalize();
		}
		Vector3 normalized = Vector3.Cross(Vector3.up, vector).normalized;
		Vector3 vector2 = transform.position - vector * Safe(settings.Distance.Value, 2.2f, 0.6f, 6f) + normalized * Safe(settings.Shoulder.Value, 0.35f, -0.8f, 0.8f) + Vector3.up * Safe(settings.Height.Value, 0.25f, -1f, 3f);
		if (settings.CollisionAvoidance.Value)
		{
			vector2 = Avoid(transform.position, vector2);
		}
		Shot shot = new Shot
		{
			Position = vector2,
			Rotation = Look(transform.position + Vector3.down * 0.15f - vector2, transform.up)
		};
		tripod = (track = shot);
		tripodSet = (trackSet = true);
		if (settings.Mode.Value == CameraMode.Dolly)
		{
			settings.Mode.Value = CameraMode.Follow;
			dollyPlaying = false;
		}
		hasPose = false;
		orbitAngle = 0f;
		RetryDesktop();
		settings.Enabled.Value = true;
		Status = "CAMERA RECENTERED";
	}

	internal void PlaceTripod()
	{
		if (camera == null || !hasPose)
		{
			Status = "ENABLE CAMERA BEFORE PLACING";
			return;
		}
		tripod = CaptureStoredPose();
		tripodSet = true;
		settings.Mode.Value = CameraMode.Tripod;
		Status = "TRIPOD PLACED";
	}

	internal void SetDollyA()
	{
		if (camera == null || !hasPose)
		{
			Status = "ENABLE CAMERA BEFORE SAVING A";
			return;
		}
		a = CaptureStoredPose();
		hasA = true;
		Status = "DOLLY A SAVED";
	}

	internal void SetDollyB()
	{
		if (camera == null || !hasPose)
		{
			Status = "ENABLE CAMERA BEFORE SAVING B";
			return;
		}
		b = CaptureStoredPose();
		hasB = true;
		Status = "DOLLY B SAVED";
	}

	internal void PlayDolly()
	{
		if (!hasA || !hasB)
		{
			Status = "SET BOTH DOLLY A AND B FIRST";
			return;
		}
		dollyStarted = Time.unscaledTime;
		dollyPlaying = true;
		settings.Mode.Value = CameraMode.Dolly;
		settings.Enabled.Value = true;
		hasPose = false;
	}

	internal void ResetView()
	{
		settings.ResetView();
		hasPose = (tripodSet = (dollyPlaying = false));
		orbitAngle = 0f;
		Status = "VIEW RESET";
	}

	internal void DrawDesktop()
	{
		if (disposed || !settings.Enabled.Value || !settings.DesktopOutput.Value || camera == null || !camera.enabled || texture == null || presenterFailed)
		{
			desktop?.Pause();
			return;
		}
		try
		{
			if (desktop == null)
			{
				desktop = new DesktopPresenter(log, source);
			}
			desktop.Draw(texture, settings.Mirror.Value, settings.Guides.Value);
		}
		catch (Exception ex)
		{
			desktop?.Pause();
			presenterFailed = true;
			log.LogError("Desktop camera presenter failed; VR camera remains unchanged: " + ex);
		}
	}

	internal void RetryDesktop()
	{
		desktop?.Dispose();
		desktop = null;
		presenterFailed = false;
	}

	internal void TakePhoto()
	{
		if (!settings.Enabled.Value || texture == null || camera == null || !camera.enabled)
		{
			Status = "ENABLE CAMERA BEFORE TAKING A PHOTO";
			return;
		}
		RenderTexture active = RenderTexture.active;
		Texture2D texture2D = null;
		try
		{
			RenderTexture.active = texture;
			texture2D = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, mipChain: false, linear: false);
			texture2D.ReadPixels(new Rect(0f, 0f, texture.width, texture.height), 0, 0, recalculateMipMaps: false);
			if (settings.Mirror.Value)
			{
				Color32[] pixels = texture2D.GetPixels32();
				for (int i = 0; i < texture.height; i++)
				{
					for (int j = 0; j < texture.width / 2; j++)
					{
						int num = i * texture.width + j;
						int num2 = i * texture.width + texture.width - 1 - j;
						Color32 color = pixels[num];
						pixels[num] = pixels[num2];
						pixels[num2] = color;
					}
				}
				texture2D.SetPixels32(pixels);
			}
			texture2D.Apply(updateMipmaps: false, makeNoLongerReadable: false);
			if (!((Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule", throwOnError: false)?.GetMethod("EncodeToPNG", BindingFlags.Public | BindingFlags.Static, null, new Type[1] { typeof(Texture2D) }, null))?.Invoke(null, new object[1] { texture2D }) is byte[] array) || array.Length < 8)
			{
				throw new InvalidOperationException("The game PNG encoder is unavailable.");
			}
			string text = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
			if (string.IsNullOrEmpty(text))
			{
				text = Paths.ConfigPath;
			}
			string text2 = Path.Combine(text, "PiglessCamera");
			Directory.CreateDirectory(text2);
			string text3 = Path.Combine(text2, "Pigless-Photo-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".png");
			using (FileStream fileStream = new FileStream(text3, FileMode.CreateNew, FileAccess.Write))
			{
				fileStream.Write(array, 0, array.Length);
			}
			LastPhoto = text3;
			Status = "PHOTO SAVED TO PICTURES / PIGLESSCAMERA";
			log.LogInfo("Photo saved: " + text3);
		}
		catch (Exception ex)
		{
			Status = "PHOTO FAILED - CHECK LOG";
			log.LogError("Photo capture failed: " + ex);
		}
		finally
		{
			RenderTexture.active = active;
			if (texture2D != null)
			{
				UnityEngine.Object.Destroy(texture2D);
			}
		}
	}

	internal void Pause()
	{
		desktop?.Pause();
		if (camera != null)
		{
			camera.enabled = false;
		}
		hasPose = false;
		Status = "CAMERA PAUSED";
	}

	public void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			Camera.onPreCull = (Camera.CameraCallback)Delegate.Remove(Camera.onPreCull, new Camera.CameraCallback(OnPreCull));
			RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
			desktop?.Dispose();
			desktop = null;
			if (camera != null)
			{
				camera.enabled = false;
				camera.targetTexture = null;
				UnityEngine.Object.Destroy(camera.gameObject);
				camera = null;
			}
			if (texture != null)
			{
				texture.Release();
				UnityEngine.Object.Destroy(texture);
				texture = null;
			}
		}
	}
}
