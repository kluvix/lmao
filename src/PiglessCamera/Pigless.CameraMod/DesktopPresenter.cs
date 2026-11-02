using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pigless.CameraMod;

internal sealed class DesktopPresenter : IDisposable
{
	private struct EnabledState
	{
		internal Behaviour Component;

		internal bool Enabled;
	}

	private struct ActiveState
	{
		internal GameObject Object;

		internal bool Active;
	}

	private readonly ManualLogSource log;

	private readonly Camera headset;

	private readonly List<EnabledState> controllers = new List<EnabledState>();

	private readonly List<ActiveState> activeStates = new List<ActiveState>();

	private Camera output;

	private Camera backup;

	private GameObject backupObject;

	private GameObject stage;

	private GameObject screen;

	private Renderer renderer;

	private readonly GameObject[] guidesObjects = new GameObject[4];

	private Material screenMaterial;

	private Material guideMaterial;

	private Vector3 originalLocalPosition;

	private Quaternion originalLocalRotation;

	private bool originalEnabled;

	private bool disposed;

	private bool hasPresented;

	private bool subscribed;

	private bool snapshotReady;

	private bool stackSnapshotReady;

	private Component additionalData;

	private PropertyInfo xrProperty;

	private PropertyInfo postProperty;

	private object originalXr;

	private object originalPost;

	private PropertyInfo renderTypeProperty;

	private PropertyInfo stackProperty;

	private object originalRenderType;

	private object baseRenderType;

	private object[] originalStack;

	private float nextSearch;

	private float nextLog;

	private float lastRenderedAt;

	private string state = "DESKTOP STARTING";

	private Texture boundTexture;

	private int lastWidth;

	private int lastHeight;

	private bool lastMirror;

	private bool lastGuides;

	private bool bindingKnown;

	private static readonly Vector3 StagePosition = new Vector3(50000f, -50000f, 50000f);

	internal bool HasPresented
	{
		get
		{
			if (!disposed && output != null && hasPresented)
			{
				return Time.unscaledTime - lastRenderedAt < 1f;
			}
			return false;
		}
	}

	internal string State
	{
		get
		{
			if (!disposed)
			{
				if (!hasPresented || HasPresented)
				{
					return state;
				}
				return "DESKTOP WAITING FOR FRAME";
			}
			return "DESKTOP OFF";
		}
	}

	internal DesktopPresenter(ManualLogSource log = null, Camera headset = null)
	{
		this.log = log;
		this.headset = headset;
	}

	internal void Draw(RenderTexture texture, bool mirror, bool guides)
	{
		if (disposed)
		{
			return;
		}
		if (texture == null || !texture.IsCreated() || Screen.width <= 0 || Screen.height <= 0)
		{
			Pause();
			return;
		}
		if (output == null)
		{
			if (backupObject != null || controllers.Count != 0)
			{
				Pause();
			}
			if (Time.unscaledTime < nextSearch)
			{
				return;
			}
			nextSearch = Time.unscaledTime + 2f;
			if (!Acquire())
			{
				return;
			}
		}
		try
		{
			EnsureStage();
			stage.SetActive(value: true);
			foreach (EnabledState controller in controllers)
			{
				if (controller.Component != null && controller.Component.enabled)
				{
					controller.Component.enabled = false;
				}
			}
			output.enabled = true;
			output.stereoTargetEye = StereoTargetEyeMask.None;
			output.targetTexture = null;
			output.targetDisplay = 0;
			output.clearFlags = CameraClearFlags.Color;
			output.backgroundColor = Color.black;
			output.cullingMask = 4;
			output.orthographic = true;
			output.orthographicSize = 1f;
			output.nearClipPlane = 0.05f;
			output.farClipPlane = 2f;
			output.useOcclusionCulling = false;
			output.allowHDR = false;
			output.allowMSAA = false;
			output.rect = new Rect(0f, 0f, 1f, 1f);
			output.depth = 10000f;
			output.transform.SetPositionAndRotation(StagePosition, Quaternion.identity);
			output.ResetProjectionMatrix();
			output.ResetWorldToCameraMatrix();
			output.ResetCullingMatrix();
			if (xrProperty != null)
			{
				xrProperty.SetValue(additionalData, false, null);
			}
			if (postProperty != null)
			{
				postProperty.SetValue(additionalData, false, null);
			}
			if (renderTypeProperty != null && !object.Equals(renderTypeProperty.GetValue(additionalData, null), baseRenderType))
			{
				renderTypeProperty.SetValue(additionalData, baseRenderType, null);
			}
			if (stackProperty?.GetValue(additionalData, null) is IList list)
			{
				if (!stackSnapshotReady)
				{
					originalStack = new object[list.Count];
					list.CopyTo(originalStack, 0);
					stackSnapshotReady = true;
				}
				if (list.Count > 0)
				{
					list.Clear();
				}
			}
			if (!bindingKnown || boundTexture != texture || lastWidth != Screen.width || lastHeight != Screen.height || lastMirror != mirror || lastGuides != guides)
			{
				Bind(texture, mirror, guides);
				bindingKnown = true;
				boundTexture = texture;
				lastWidth = Screen.width;
				lastHeight = Screen.height;
				lastMirror = mirror;
				lastGuides = guides;
			}
			if (!hasPresented)
			{
				state = "DESKTOP STARTING";
			}
		}
		catch
		{
			Pause();
			state = "DESKTOP ERROR - TOGGLE DESKTOP";
			throw;
		}
	}

	private bool Acquire()
	{
		GorillaCameraFollow[] array = Resources.FindObjectsOfTypeAll<GorillaCameraFollow>();
		Camera camera = null;
		GameObject gameObject = null;
		int num = 0;
		StringBuilder stringBuilder = new StringBuilder();
		GorillaCameraFollow[] array2 = array;
		foreach (GorillaCameraFollow val in array2)
		{
			if ((UnityEngine.Object)(object)val == null || val.cameraParent == null || !((Component)(object)val).gameObject.scene.IsValid() || !val.cameraParent.scene.IsValid())
			{
				continue;
			}
			GameObject cameraParent = val.cameraParent;
			Camera[] componentsInChildren = cameraParent.GetComponentsInChildren<Camera>(includeInactive: true);
			foreach (Camera camera2 in componentsInChildren)
			{
				num++;
				if (camera2 == null)
				{
					continue;
				}
				if (num <= 8)
				{
					stringBuilder.Append(camera2.name).Append(" [eye=").Append(camera2.stereoTargetEye)
						.Append(", RT=")
						.Append(camera2.targetTexture != null)
						.Append(", active=")
						.Append(camera2.gameObject.activeInHierarchy)
						.Append("]; ");
				}
				if (!(camera2 == headset) && !(camera2 == GTFirstPersonCamera.camera) && (!(headset != null) || !(camera2.gameObject == headset.gameObject)) && (!(GTFirstPersonCamera.camera != null) || !(camera2.gameObject == GTFirstPersonCamera.camera.gameObject)) && !(camera2.targetTexture != null) && IsMonoPcCamera(camera2) && camera2.targetDisplay == 0)
				{
					camera = camera2;
					gameObject = cameraParent;
					if (camera2.enabled && camera2.gameObject.activeInHierarchy)
					{
						break;
					}
				}
			}
			if (camera != null)
			{
				break;
			}
		}
		if (camera == null)
		{
			state = "DESKTOP CAMERA NOT FOUND";
			if (Time.unscaledTime >= nextLog)
			{
				nextLog = Time.unscaledTime + 10f;
				log?.LogWarning("Desktop output: no dedicated mono PC camera found under GorillaCameraFollow.cameraParent. Checked " + array.Length + " PC-camera branches / " + num + " cameras. Candidates: " + stringBuilder?.ToString() + "Headset and XR-enabled cameras are left untouched.");
			}
			return false;
		}
		output = camera;
		snapshotReady = false;
		originalLocalPosition = output.transform.localPosition;
		originalLocalRotation = output.transform.localRotation;
		originalEnabled = output.enabled;
		try
		{
			backupObject = new GameObject("Pigless desktop / saved camera state");
			backup = backupObject.AddComponent<Camera>();
			backup.enabled = false;
			backup.CopyFrom(output);
			backup.enabled = false;
			snapshotReady = true;
			backupObject.SetActive(value: false);
			UnityEngine.Object.DontDestroyOnLoad(backupObject);
			Behaviour[] components = output.GetComponents<Behaviour>();
			foreach (Behaviour behaviour in components)
			{
				if (behaviour != null && (behaviour.GetType().Name == "CinemachineBrain" || behaviour.GetType().Name == "AudioListener"))
				{
					controllers.Add(new EnabledState
					{
						Component = behaviour,
						Enabled = behaviour.enabled
					});
					behaviour.enabled = false;
				}
			}
			Transform transform = output.transform;
			while (transform != null)
			{
				activeStates.Add(new ActiveState
				{
					Object = transform.gameObject,
					Active = transform.gameObject.activeSelf
				});
				transform.gameObject.SetActive(value: true);
				if (transform == gameObject.transform)
				{
					break;
				}
				transform = transform.parent;
			}
			if (!output.gameObject.activeInHierarchy)
			{
				throw new InvalidOperationException("The dedicated PC camera's parent is inactive.");
			}
			Type type = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime", throwOnError: false);
			if (type != null)
			{
				additionalData = output.GetComponent(type);
				if (additionalData != null)
				{
					xrProperty = WritableBool(type, "allowXRRendering");
					postProperty = WritableBool(type, "renderPostProcessing");
					if (xrProperty != null)
					{
						originalXr = xrProperty.GetValue(additionalData, null);
					}
					if (postProperty != null)
					{
						originalPost = postProperty.GetValue(additionalData, null);
					}
					renderTypeProperty = type.GetProperty("renderType");
					if (renderTypeProperty != null && renderTypeProperty.CanRead && renderTypeProperty.CanWrite && renderTypeProperty.PropertyType.IsEnum)
					{
						originalRenderType = renderTypeProperty.GetValue(additionalData, null);
						baseRenderType = Enum.Parse(renderTypeProperty.PropertyType, "Base");
					}
					else
					{
						renderTypeProperty = null;
					}
					stackProperty = type.GetProperty("cameraStack");
					if (stackProperty == null || !stackProperty.CanRead)
					{
						stackProperty = null;
					}
					if ((originalRenderType == null || object.Equals(originalRenderType, baseRenderType)) && stackProperty?.GetValue(additionalData, null) is IList list)
					{
						originalStack = new object[list.Count];
						list.CopyTo(originalStack, 0);
						stackSnapshotReady = true;
					}
				}
			}
			Camera.onPostRender = (Camera.CameraCallback)Delegate.Combine(Camera.onPostRender, new Camera.CameraCallback(OnPostRender));
			RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
			subscribed = true;
			hasPresented = false;
			bindingKnown = false;
			state = "DESKTOP STARTING";
			log?.LogInfo("Desktop output acquired game's mono PC camera: " + output.name + " under " + gameObject.name + ". Presenting filming texture through the PC camera; headset unchanged.");
			return true;
		}
		catch
		{
			Pause();
			throw;
		}
	}

	private static PropertyInfo WritableBool(Type type, string name)
	{
		PropertyInfo property = type.GetProperty(name);
		if (!(property != null) || !property.CanRead || !property.CanWrite || !(property.PropertyType == typeof(bool)))
		{
			return null;
		}
		return property;
	}

	private static bool IsMonoPcCamera(Camera camera)
	{
		if (camera.stereoTargetEye == StereoTargetEyeMask.None)
		{
			return true;
		}
		Type type = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime", throwOnError: false);
		if (type == null)
		{
			return false;
		}
		Component component = camera.GetComponent(type);
		PropertyInfo property = type.GetProperty("allowXRRendering");
		if (component != null && property != null && property.PropertyType == typeof(bool))
		{
			return !(bool)property.GetValue(component, null);
		}
		return false;
	}

	private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
	{
		MarkRendered(camera);
	}

	private void OnPostRender(Camera camera)
	{
		MarkRendered(camera);
	}

	private void MarkRendered(Camera camera)
	{
		if (!disposed && !(output == null) && !(camera != output))
		{
			if (!hasPresented)
			{
				log?.LogInfo("Desktop feed rendered by game's PC camera: " + camera.name + "; same filming texture as tablet preview.");
			}
			hasPresented = true;
			lastRenderedAt = Time.unscaledTime;
			state = "DESKTOP ON";
		}
	}

	private void EnsureStage()
	{
		if (stage != null)
		{
			return;
		}
		stage = new GameObject("Pigless desktop / isolated presentation stage");
		stage.layer = 2;
		stage.transform.SetPositionAndRotation(StagePosition, Quaternion.identity);
		UnityEngine.Object.DontDestroyOnLoad(stage);
		screen = Quad("Desktop filming screen", stage.transform, new Vector3(0f, 0f, 1f));
		renderer = screen.GetComponent<Renderer>();
		screenMaterial = Material(Color.white, transparent: false);
		renderer.sharedMaterial = screenMaterial;
		log?.LogInfo("Desktop presentation shader: " + screenMaterial.shader.name + ".");
		for (int i = 0; i < guidesObjects.Length; i++)
		{
			guidesObjects[i] = Quad("Desktop composition guide", stage.transform, new Vector3(0f, 0f, 0.99f));
			if (guideMaterial == null)
			{
				guideMaterial = Material(new Color(1f, 1f, 1f, 0.42f), transparent: true);
			}
			guidesObjects[i].GetComponent<Renderer>().sharedMaterial = guideMaterial;
			guidesObjects[i].SetActive(value: false);
		}
	}

	private static GameObject Quad(string name, Transform parent, Vector3 position)
	{
		GameObject gameObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
		gameObject.name = name;
		gameObject.layer = 2;
		gameObject.transform.SetParent(parent, worldPositionStays: false);
		gameObject.transform.localPosition = position;
		Collider component = gameObject.GetComponent<Collider>();
		if (component != null)
		{
			component.enabled = false;
			UnityEngine.Object.Destroy(component);
		}
		return gameObject;
	}

	private static Material Material(Color color, bool transparent)
	{
		Material material = null;
		string[] array = ((!transparent) ? new string[4] { "Universal Render Pipeline/Unlit", "Unlit/Texture", "Sprites/Default", "UI/Default" } : new string[3] { "Universal Render Pipeline/Unlit", "Sprites/Default", "UI/Default" });
		for (int i = 0; i < array.Length; i++)
		{
			Shader shader = Shader.Find(array[i]);
			if (!(shader == null) && shader.isSupported)
			{
				Material material2 = new Material(shader)
				{
					name = "Pigless desktop presentation"
				};
				if (material2.HasProperty("_BaseMap") || material2.HasProperty("_MainTex"))
				{
					material = material2;
					break;
				}
				UnityEngine.Object.Destroy(material2);
			}
		}
		if (material == null)
		{
			throw new InvalidOperationException("No textured desktop presentation shader is available.");
		}
		if (material.HasProperty("_BaseColor"))
		{
			material.SetColor("_BaseColor", color);
		}
		if (material.HasProperty("_Color"))
		{
			material.SetColor("_Color", color);
		}
		if (material.HasProperty("_Cull"))
		{
			material.SetFloat("_Cull", 0f);
		}
		if (material.HasProperty("_Surface"))
		{
			material.SetFloat("_Surface", transparent ? 1 : 0);
		}
		if (material.HasProperty("_ZWrite"))
		{
			material.SetFloat("_ZWrite", (!transparent) ? 1 : 0);
		}
		if (material.HasProperty("_SrcBlend"))
		{
			material.SetFloat("_SrcBlend", (!transparent) ? 1 : 5);
		}
		if (material.HasProperty("_DstBlend"))
		{
			material.SetFloat("_DstBlend", transparent ? 10 : 0);
		}
		if (transparent)
		{
			material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
			material.renderQueue = 3000;
		}
		return material;
	}

	private void Bind(RenderTexture texture, bool mirror, bool guides)
	{
		output.aspect = (float)Screen.width / (float)Screen.height;
		FrameRect frameRect = CameraMath.FitFrame(Screen.width, Screen.height, texture.width, texture.height);
		float num = 2f * output.aspect * frameRect.Width / (float)Screen.width;
		float num2 = 2f * frameRect.Height / (float)Screen.height;
		screen.transform.localScale = new Vector3(num, num2, 1f);
		string[] array = new string[2] { "_BaseMap", "_MainTex" };
		foreach (string name in array)
		{
			if (screenMaterial.HasProperty(name))
			{
				screenMaterial.SetTexture(name, texture);
				screenMaterial.SetTextureScale(name, new Vector2((!mirror) ? 1 : (-1), 1f));
				screenMaterial.SetTextureOffset(name, new Vector2(mirror ? 1 : 0, 0f));
			}
		}
		for (int j = 0; j < 4; j++)
		{
			GameObject gameObject = guidesObjects[j];
			gameObject.SetActive(guides);
			if (guides)
			{
				bool flag = j < 2;
				float num3 = (float)(j % 2 + 1) / 3f - 0.5f;
				gameObject.transform.localPosition = new Vector3(flag ? (num * num3) : 0f, flag ? 0f : (num2 * num3), 0.99f);
				gameObject.transform.localScale = new Vector3(flag ? (2f * output.aspect / (float)Screen.width) : num, flag ? num2 : (2f / (float)Screen.height), 1f);
			}
		}
	}

	internal void Pause()
	{
		if (subscribed)
		{
			Camera.onPostRender = (Camera.CameraCallback)Delegate.Remove(Camera.onPostRender, new Camera.CameraCallback(OnPostRender));
			RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
			subscribed = false;
		}
		if (output != null && snapshotReady)
		{
			try
			{
				output.enabled = false;
				if (backup != null)
				{
					output.CopyFrom(backup);
				}
				output.transform.localPosition = originalLocalPosition;
				output.transform.localRotation = originalLocalRotation;
				if (additionalData != null)
				{
					if (stackSnapshotReady && stackProperty?.GetValue(additionalData, null) is IList list)
					{
						list.Clear();
						if (originalStack != null)
						{
							object[] array = originalStack;
							foreach (object value in array)
							{
								list.Add(value);
							}
						}
					}
					if (renderTypeProperty != null)
					{
						renderTypeProperty.SetValue(additionalData, originalRenderType, null);
					}
					if (xrProperty != null)
					{
						xrProperty.SetValue(additionalData, originalXr, null);
					}
					if (postProperty != null)
					{
						postProperty.SetValue(additionalData, originalPost, null);
					}
				}
				output.enabled = originalEnabled;
			}
			catch (Exception ex)
			{
				log?.LogWarning("Restoring the PC camera reported an error: " + ex);
			}
			finally
			{
				if (output != null)
				{
					output.enabled = originalEnabled;
				}
				foreach (EnabledState controller in controllers)
				{
					if (controller.Component != null)
					{
						controller.Component.enabled = controller.Enabled;
					}
				}
				for (int num = activeStates.Count - 1; num >= 0; num--)
				{
					if (activeStates[num].Object != null)
					{
						activeStates[num].Object.SetActive(activeStates[num].Active);
					}
				}
			}
		}
		output = null;
		backup = null;
		additionalData = null;
		xrProperty = (postProperty = null);
		originalXr = (originalPost = null);
		snapshotReady = false;
		stackSnapshotReady = false;
		renderTypeProperty = (stackProperty = null);
		originalRenderType = (baseRenderType = null);
		originalStack = null;
		controllers.Clear();
		activeStates.Clear();
		if (backupObject != null)
		{
			UnityEngine.Object.Destroy(backupObject);
		}
		backupObject = null;
		if (stage != null)
		{
			stage.SetActive(value: false);
		}
		hasPresented = false;
		bindingKnown = false;
		boundTexture = null;
		state = "DESKTOP OFF";
		nextSearch = 0f;
	}

	public void Dispose()
	{
		if (!disposed)
		{
			Pause();
			disposed = true;
			if (stage != null)
			{
				UnityEngine.Object.Destroy(stage);
			}
			stage = null;
			if (screenMaterial != null)
			{
				UnityEngine.Object.Destroy(screenMaterial);
			}
			screenMaterial = null;
			if (guideMaterial != null)
			{
				UnityEngine.Object.Destroy(guideMaterial);
			}
			guideMaterial = null;
		}
	}
}
