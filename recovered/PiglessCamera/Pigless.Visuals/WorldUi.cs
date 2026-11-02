using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace Pigless.Visuals;

internal sealed class WorldUi : IDisposable
{
	private sealed class LabelState
	{
		internal float Height;

		internal float MaximumWidth;

		internal Material Material;

		internal int LayoutPasses;
	}

	private readonly ManualLogSource logger;

	private readonly List<Material> materials = new List<Material>();

	private readonly List<Mesh> meshes = new List<Mesh>();

	private readonly List<GameObject> objects = new List<GameObject>();

	private readonly Dictionary<Component, LabelState> labels = new Dictionary<Component, LabelState>();

	private Type textType;

	private Type fontType;

	private Type fontStyleType;

	private UnityEngine.Object font;

	private Material sourceFontMaterial;

	private MethodInfo requestGlyphs;

	private bool disposed;

	private bool fontFailureLogged;

	private const int FontSize = 128;

	internal Transform Root { get; }

	internal WorldUi(string name, ManualLogSource logger)
	{
		this.logger = logger ?? throw new ArgumentNullException("logger");
		Root = new GameObject(string.IsNullOrEmpty(name) ? "Pigless World UI" : name).transform;
	}

	internal Transform Group(string name, Transform parent = null)
	{
		return Node(name, Vector3.zero, parent).transform;
	}

	internal Renderer Panel(string name, Vector2 size, float radius, float thickness, Vector3 localPosition, Color color, Transform parent = null)
	{
		GameObject gameObject = Node(name, localPosition, parent);
		Mesh mesh = RoundedPanel(size, radius, thickness);
		meshes.Add(mesh);
		gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
		MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
		meshRenderer.sharedMaterial = MakeMaterial(color, null);
		return meshRenderer;
	}

	internal Renderer Geometry(string name, Vector3[] vertices, int[] triangles, Color color, Transform parent = null)
	{
		if (vertices == null)
		{
			throw new ArgumentNullException("vertices");
		}
		if (triangles == null)
		{
			throw new ArgumentNullException("triangles");
		}
		GameObject gameObject = Node(name, Vector3.zero, parent);
		Mesh mesh = new Mesh
		{
			name = name
		};
		meshes.Add(mesh);
		mesh.vertices = vertices;
		mesh.triangles = triangles;
		mesh.RecalculateNormals();
		mesh.RecalculateBounds();
		gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
		MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
		meshRenderer.sharedMaterial = MakeMaterial(color, null);
		return meshRenderer;
	}

	internal GameObject Primitive(string name, PrimitiveType type, Vector3 localPosition, Vector3 scale, Color color, bool collision = false, Transform parent = null)
	{
		EnsureAlive();
		GameObject gameObject = GameObject.CreatePrimitive(type);
		gameObject.name = name;
		objects.Add(gameObject);
		gameObject.transform.SetParent((parent != null) ? parent : Root, worldPositionStays: false);
		gameObject.layer = ((parent != null) ? parent : Root).gameObject.layer;
		gameObject.transform.localPosition = localPosition;
		gameObject.transform.localRotation = Quaternion.identity;
		gameObject.transform.localScale = scale;
		Renderer component = gameObject.GetComponent<Renderer>();
		if (component != null)
		{
			component.sharedMaterial = MakeMaterial(color, null);
		}
		if (!collision)
		{
			Collider component2 = gameObject.GetComponent<Collider>();
			if (component2 != null)
			{
				component2.enabled = false;
				UnityEngine.Object.Destroy(component2);
			}
		}
		return gameObject;
	}

	internal Component Label(string text, Vector3 localPosition, float height, Color color, string anchor = "MiddleCenter", float maxWidth = 0f, Transform parent = null)
	{
		EnsureFont();
		GameObject gameObject = Node("Label " + text, localPosition, parent);
		MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
		Component component = gameObject.AddComponent(textType);
		Set(component, "font", font);
		Set(component, "fontSize", 128);
		Set(component, "characterSize", 1f);
		SetEnum(component, "fontStyle", "Bold");
		SetEnum(component, "anchor", anchor);
		string enumValue = ((anchor.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0) ? "Left" : ((anchor.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0) ? "Right" : "Center"));
		SetEnum(component, "alignment", enumValue);
		Set(component, "richText", false);
		Set(component, "color", color);
		RequestGlyphs(text ?? "");
		Set(component, "text", text ?? "");
		Shader shader = Shader.Find("Sprites/Default");
		Material material = ((shader != null && shader.isSupported) ? new Material(shader)
		{
			name = "Pigless Depth-Tested Text"
		} : new Material(sourceFontMaterial)
		{
			name = "Pigless Text"
		});
		if (material.HasProperty("_Color"))
		{
			material.SetColor("_Color", Color.white);
		}
		if (material.HasProperty("_RendererColor"))
		{
			material.SetColor("_RendererColor", Color.white);
		}
		material.renderQueue = 3000;
		materials.Add(material);
		ConfigureTextMaterial(material);
		meshRenderer.sharedMaterial = material;
		labels.Add(component, new LabelState
		{
			Height = Mathf.Max(0.001f, height),
			MaximumWidth = Mathf.Max(0f, maxWidth),
			Material = material,
			LayoutPasses = 3
		});
		FitLabel(component);
		return component;
	}

	internal void SetText(Component label, string text)
	{
		if (label == null || disposed)
		{
			return;
		}
		string text2 = text ?? "";
		if (!string.Equals(textType.GetProperty("text")?.GetValue(label, null) as string, text2, StringComparison.Ordinal))
		{
			RequestGlyphs(text2);
			Set(label, "text", text2);
			if (labels.TryGetValue(label, out var value))
			{
				value.LayoutPasses = 2;
			}
			FitLabel(label);
		}
	}

	internal void RefreshLayout()
	{
		if (disposed)
		{
			return;
		}
		foreach (KeyValuePair<Component, LabelState> label in labels)
		{
			if (label.Key != null && label.Value.LayoutPasses > 0)
			{
				FitLabel(label.Key);
				label.Value.LayoutPasses--;
			}
		}
	}

	internal void SetTextColor(Component label, Color color)
	{
		if (label != null && !disposed)
		{
			Set(label, "color", color);
		}
	}

	internal Renderer Image(string name, Texture texture, Vector2 size, Vector3 localPosition, Transform parent = null)
	{
		GameObject gameObject = Node(name, localPosition, parent);
		float num = Mathf.Max(0.001f, size.x) * 0.5f;
		float num2 = Mathf.Max(0.001f, size.y) * 0.5f;
		Mesh mesh = new Mesh
		{
			name = "Pigless Image Plane"
		};
		mesh.vertices = new Vector3[4]
		{
			new Vector3(0f - num, 0f - num2, 0f),
			new Vector3(num, 0f - num2, 0f),
			new Vector3(num, num2, 0f),
			new Vector3(0f - num, num2, 0f)
		};
		mesh.uv = new Vector2[4]
		{
			new Vector2(0f, 0f),
			new Vector2(1f, 0f),
			new Vector2(1f, 1f),
			new Vector2(0f, 1f)
		};
		mesh.triangles = new int[6] { 0, 2, 1, 0, 3, 2 };
		mesh.RecalculateNormals();
		mesh.RecalculateBounds();
		meshes.Add(mesh);
		gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
		MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
		meshRenderer.sharedMaterial = MakeMaterial(Color.white, texture, requireTexture: true);
		return meshRenderer;
	}

	internal void SetColor(Renderer renderer, Color color)
	{
		if (!(renderer == null) && !disposed)
		{
			Material sharedMaterial = renderer.sharedMaterial;
			if (sharedMaterial != null)
			{
				ApplyColor(sharedMaterial, color);
			}
		}
	}

	private GameObject Node(string name, Vector3 position, Transform parent)
	{
		EnsureAlive();
		GameObject gameObject = new GameObject(name);
		objects.Add(gameObject);
		gameObject.transform.SetParent((parent != null) ? parent : Root, worldPositionStays: false);
		gameObject.layer = ((parent != null) ? parent : Root).gameObject.layer;
		gameObject.transform.localPosition = position;
		gameObject.transform.localRotation = Quaternion.identity;
		gameObject.transform.localScale = Vector3.one;
		return gameObject;
	}

	private Material MakeMaterial(Color color, Texture texture, bool requireTexture = false)
	{
		bool flag = texture != null || requireTexture;
		string[] obj = ((!flag) ? new string[4] { "Universal Render Pipeline/Unlit", "Unlit/Color", "Sprites/Default", "UI/Default" } : new string[4] { "Universal Render Pipeline/Unlit", "Unlit/Texture", "Sprites/Default", "UI/Default" });
		Material material = null;
		string[] array = obj;
		for (int i = 0; i < array.Length; i++)
		{
			Shader shader = Shader.Find(array[i]);
			if (!(shader == null) && shader.isSupported)
			{
				Material material2 = new Material(shader)
				{
					name = "Pigless " + shader.name
				};
				if (!flag || material2.HasProperty("_BaseMap") || material2.HasProperty("_MainTex"))
				{
					material = material2;
					break;
				}
				UnityEngine.Object.Destroy(material2);
			}
		}
		if (material == null)
		{
			throw new InvalidOperationException("No usable unlit UI shader is present in this game build.");
		}
		materials.Add(material);
		ApplyColor(material, color);
		if (texture != null)
		{
			if (material.HasProperty("_BaseMap"))
			{
				material.SetTexture("_BaseMap", texture);
			}
			if (material.HasProperty("_MainTex"))
			{
				material.SetTexture("_MainTex", texture);
			}
		}
		if (material.HasProperty("_Cull"))
		{
			material.SetFloat("_Cull", 0f);
		}
		if (color.a < 0.999f || texture != null)
		{
			if (material.HasProperty("_Surface"))
			{
				material.SetFloat("_Surface", 1f);
			}
			if (material.HasProperty("_SrcBlend"))
			{
				material.SetFloat("_SrcBlend", 5f);
			}
			if (material.HasProperty("_DstBlend"))
			{
				material.SetFloat("_DstBlend", 10f);
			}
			if (material.HasProperty("_ZWrite"))
			{
				material.SetFloat("_ZWrite", 0f);
			}
			material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
			material.SetOverrideTag("RenderType", "Transparent");
			material.renderQueue = 3000;
		}
		return material;
	}

	private static void ApplyColor(Material material, Color color)
	{
		if (material.HasProperty("_BaseColor"))
		{
			material.SetColor("_BaseColor", color);
		}
		if (material.HasProperty("_Color"))
		{
			material.SetColor("_Color", color);
		}
	}

	private static Mesh RoundedPanel(Vector2 requested, float radius, float thickness)
	{
		float num = Mathf.Max(0.001f, requested.x);
		float num2 = Mathf.Max(0.001f, requested.y);
		radius = Mathf.Clamp(radius, 0f, Mathf.Min(num, num2) * 0.5f);
		float num3 = Mathf.Max(0.0005f, thickness) * 0.5f;
		List<Vector2> list = new List<Vector2>(32);
		Vector2[] array = new Vector2[4]
		{
			new Vector2(num * 0.5f - radius, num2 * 0.5f - radius),
			new Vector2((0f - num) * 0.5f + radius, num2 * 0.5f - radius),
			new Vector2((0f - num) * 0.5f + radius, (0f - num2) * 0.5f + radius),
			new Vector2(num * 0.5f - radius, (0f - num2) * 0.5f + radius)
		};
		for (int i = 0; i < 4; i++)
		{
			for (int j = 0; j <= 7; j++)
			{
				float f = ((float)i * 90f + (float)j * 90f / 7f) * (MathF.PI / 180f);
				list.Add(array[i] + new Vector2(Mathf.Cos(f), Mathf.Sin(f)) * radius);
			}
		}
		int count = list.Count;
		Vector3[] array2 = new Vector3[2 * (count + 1)];
		Vector2[] array3 = new Vector2[array2.Length];
		array2[0] = new Vector3(0f, 0f, 0f - num3);
		array2[count + 1] = new Vector3(0f, 0f, num3);
		array3[0] = (array3[count + 1] = new Vector2(0.5f, 0.5f));
		for (int k = 0; k < count; k++)
		{
			array2[k + 1] = new Vector3(list[k].x, list[k].y, 0f - num3);
			array2[count + k + 2] = new Vector3(list[k].x, list[k].y, num3);
			array3[k + 1] = (array3[count + k + 2] = new Vector2(list[k].x / num + 0.5f, list[k].y / num2 + 0.5f));
		}
		int[] array4 = new int[count * 12];
		for (int l = 0; l < count; l++)
		{
			int num4 = (l + 1) % count;
			int num5 = l + 1;
			int num6 = num4 + 1;
			int num7 = count + l + 2;
			int num8 = count + num4 + 2;
			int num9 = l * 12;
			array4[num9] = 0;
			array4[num9 + 1] = num6;
			array4[num9 + 2] = num5;
			array4[num9 + 3] = count + 1;
			array4[num9 + 4] = num7;
			array4[num9 + 5] = num8;
			array4[num9 + 6] = num5;
			array4[num9 + 7] = num6;
			array4[num9 + 8] = num8;
			array4[num9 + 9] = num5;
			array4[num9 + 10] = num8;
			array4[num9 + 11] = num7;
		}
		Mesh mesh = new Mesh();
		mesh.name = "Pigless Rounded Panel";
		mesh.vertices = array2;
		mesh.uv = array3;
		mesh.triangles = array4;
		mesh.RecalculateNormals();
		mesh.RecalculateBounds();
		return mesh;
	}

	private void EnsureFont()
	{
		EnsureAlive();
		if (font != null)
		{
			return;
		}
		try
		{
			textType = Type.GetType("UnityEngine.TextMesh, UnityEngine.TextRenderingModule", throwOnError: false);
			fontType = Type.GetType("UnityEngine.Font, UnityEngine.TextRenderingModule", throwOnError: false);
			fontStyleType = Type.GetType("UnityEngine.FontStyle, UnityEngine.TextRenderingModule", throwOnError: false);
			if (textType == null || fontType == null)
			{
				throw new InvalidOperationException("The game's 3D text module is unavailable.");
			}
			string[] array = new string[2] { "LegacyRuntime.ttf", "Arial.ttf" };
			foreach (string path in array)
			{
				try
				{
					font = Resources.GetBuiltinResource(fontType, path);
				}
				catch
				{
				}
				if (font != null)
				{
					break;
				}
			}
			if (font == null)
			{
				UnityEngine.Object[] array2 = Resources.FindObjectsOfTypeAll(fontType);
				if (array2 != null && array2.Length != 0)
				{
					font = array2[0];
				}
			}
			if (font == null)
			{
				throw new InvalidOperationException("No built-in or loaded font is available for Pigless UI.");
			}
			if (fontStyleType != null)
			{
				requestGlyphs = fontType.GetMethod("RequestCharactersInTexture", new Type[3]
				{
					typeof(string),
					typeof(int),
					fontStyleType
				});
			}
			RequestGlyphs(" !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~•—→×✓▶●");
			sourceFontMaterial = fontType.GetProperty("material")?.GetValue(font, null) as Material;
			if (sourceFontMaterial == null)
			{
				throw new InvalidOperationException("The selected font has no rendering material.");
			}
		}
		catch (Exception ex)
		{
			if (!fontFailureLogged)
			{
				fontFailureLogged = true;
				logger.LogError("Pigless UI font setup failed: " + ex.GetBaseException());
			}
			font = null;
			throw;
		}
	}

	private void RequestGlyphs(string text)
	{
		if (font == null || requestGlyphs == null)
		{
			return;
		}
		requestGlyphs.Invoke(font, new object[3]
		{
			text,
			128,
			Enum.Parse(fontStyleType, "Bold")
		});
		sourceFontMaterial = (fontType.GetProperty("material")?.GetValue(font, null) as Material) ?? sourceFontMaterial;
		if (sourceFontMaterial == null)
		{
			return;
		}
		foreach (LabelState value in labels.Values)
		{
			if (value.Material != null)
			{
				ConfigureTextMaterial(value.Material);
			}
		}
	}

	private void ConfigureTextMaterial(Material material)
	{
		Texture texture = ((sourceFontMaterial != null) ? sourceFontMaterial.mainTexture : null);
		if (texture != null)
		{
			if (material.HasProperty("_MainTex"))
			{
				material.SetTexture("_MainTex", texture);
			}
			if (material.HasProperty("_BaseMap"))
			{
				material.SetTexture("_BaseMap", texture);
			}
		}
		if (material.HasProperty("_ZTest"))
		{
			material.SetFloat("_ZTest", 4f);
		}
		if (material.HasProperty("_ZWrite"))
		{
			material.SetFloat("_ZWrite", 0f);
		}
		if (material.HasProperty("_Cull"))
		{
			material.SetFloat("_Cull", 0f);
		}
	}

	private void FitLabel(Component label)
	{
		if (labels.TryGetValue(label, out var value))
		{
			Renderer component = label.GetComponent<Renderer>();
			Vector3 obj = ((component != null) ? component.localBounds.size : Vector3.zero);
			float num = obj.y;
			float num2 = obj.x;
			if (num < 1E-05f)
			{
				num = 12.8f;
				string text = (textType.GetProperty("text")?.GetValue(label, null) as string) ?? "";
				num2 = (float)Mathf.Max(1, text.Length) * num * 0.56f;
			}
			float num3 = value.Height / num;
			if (value.MaximumWidth > 0f && num2 > 1E-05f)
			{
				num3 = Mathf.Min(num3, value.MaximumWidth / num2);
			}
			label.transform.localScale = Vector3.one * num3;
		}
	}

	private static void Set(Component component, string name, object value)
	{
		PropertyInfo property = component.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
		if (property == null || !property.CanWrite)
		{
			throw new MissingMemberException(component.GetType().FullName, name);
		}
		property.SetValue(component, value, null);
	}

	private static void SetEnum(Component component, string propertyName, string enumValue)
	{
		PropertyInfo property = component.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
		if (property == null || !property.CanWrite)
		{
			throw new MissingMemberException(component.GetType().FullName, propertyName);
		}
		property.SetValue(component, Enum.Parse(property.PropertyType, enumValue, ignoreCase: true), null);
	}

	private void EnsureAlive()
	{
		if (disposed)
		{
			throw new ObjectDisposedException("WorldUi");
		}
		if (Root == null)
		{
			throw new InvalidOperationException("The Pigless UI root was destroyed by a scene change.");
		}
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}
		disposed = true;
		foreach (GameObject @object in objects)
		{
			if (@object != null && (Root == null || !@object.transform.IsChildOf(Root)))
			{
				UnityEngine.Object.Destroy(@object);
			}
		}
		if (Root != null)
		{
			UnityEngine.Object.Destroy(Root.gameObject);
		}
		foreach (Material material in materials)
		{
			if (material != null)
			{
				UnityEngine.Object.Destroy(material);
			}
		}
		foreach (Mesh mesh in meshes)
		{
			if (mesh != null)
			{
				UnityEngine.Object.Destroy(mesh);
			}
		}
		labels.Clear();
		objects.Clear();
		materials.Clear();
		meshes.Clear();
	}
}
