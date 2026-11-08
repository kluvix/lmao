using System;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace HeyJace;

// A small chat notification rendered in the local headset view. The game's
// desktop mirror can include it; this creates no desktop window or GUI overlay.
internal sealed class JaceHud : IDisposable
{
    private string message = "";
    private float shownAt, until, nextCameraCheck, nextTextAttempt;
    private bool hasMessage, persistent, textDirty = true;
    private int fitFrames, lineCount = 1;
    private GameObject? root;
    private TextMesh? foreground, shadow;
    private Material? textMaterial, shadowMaterial;
    private Font? font;
    private Camera? camera;
    private MemberInfo? playerInstance, mainCamera;
    private AudioSource? audio;
    private AudioClip? chime;

    internal void Show(string heading, string text, float seconds = 6)
    {
        persistent = false;
        ShowCore(text, seconds);
    }

    internal void ShowPersistent(string heading, string text)
    {
        persistent = true;
        ShowCore(text, 15);
    }

    internal void ClearPersistent()
    {
        persistent = false;
        until = Time.unscaledTime + 6;
    }

    private void ShowCore(string text, float seconds)
    {
        text ??= "";
        // Incoming text cannot inject TextMesh formatting. Only our cyan tag
        // below uses rich text; the shadow always receives plain text.
        text = text.Replace('<', '[').Replace('>', ']').Replace('\r', ' ').Replace('\n', ' ');
        message = text.Length > 240 ? text.Substring(0, 237) + "..." : text;
        shownAt = Time.unscaledTime;
        until = shownAt + Mathf.Clamp(seconds, 2, 30);
        hasMessage = true;
        textDirty = true;
    }

    internal void Wake(bool playSound, string greeting = "What's up?")
    {
        Show("JACE", greeting, 10);
        if (!playSound) return;
        EnsureSound();
        audio?.PlayOneShot(chime, .42f);
    }

    private float Opacity
    {
        get
        {
            if (!hasMessage) return 0;
            float now = Time.unscaledTime;
            float enter = Mathf.SmoothStep(0, 1, Mathf.Clamp01((now - shownAt) / .12f));
            float exit = persistent ? 1 : Mathf.SmoothStep(0, 1, Mathf.Clamp01((until - now) / .25f));
            return enter * exit;
        }
    }

    internal void Tick()
    {
        if (camera == null || Time.unscaledTime >= nextCameraCheck)
        {
            nextCameraCheck = Time.unscaledTime + 1;
            camera = FindLocalCamera();
        }
        if (root == null && Time.unscaledTime >= nextTextAttempt)
        {
            nextTextAttempt = Time.unscaledTime + 5;
            try { EnsureText(); }
            catch { ClearText(); throw; }
        }
        float opacity = Opacity;
        bool visible = opacity > .001f && camera != null;
        root?.SetActive(visible);
        if (!visible || root == null || foreground == null || shadow == null || camera == null) return;

        float distance = Mathf.Max(.85f, camera.nearClipPlane * 4);
        float halfHeight = Mathf.Tan(Mathf.Clamp(camera.fieldOfView, 55, 115) * Mathf.Deg2Rad * .5f) * distance;
        float aspect = camera.stereoEnabled ? 1 : Mathf.Clamp(camera.aspect, .75f, 1.8f);
        float halfWidth = halfHeight * aspect;
        // Inset to 26% across the view so the line remains inside the shared
        // visible area of both headset eyes instead of clipping at the left.
        root.transform.SetPositionAndRotation(camera.transform.TransformPoint(new Vector3(-.48f * halfWidth, -.48f * halfHeight, distance)), camera.transform.rotation);
        root.transform.localScale = Vector3.one * halfHeight;

        if (textDirty)
        {
            string plain = Wrap("[Jace] " + message, 52, out lineCount);
            font?.RequestCharactersInTexture(plain, 64, FontStyle.Bold);
            foreground.text = "<color=#6BE8FF>[Jace]</color>" + plain.Substring(6);
            shadow.text = plain;
            textDirty = false;
            fitFrames = 3;
        }
        if (font != null)
        {
            if (textMaterial != null) textMaterial.mainTexture = font.material.mainTexture;
            if (shadowMaterial != null) shadowMaterial.mainTexture = font.material.mainTexture;
        }
        if (fitFrames > 0)
        {
            fitFrames--;
            Bounds bounds = foreground.GetComponent<Renderer>().localBounds;
            if (bounds.size.x > .001f && bounds.size.y > .001f)
            {
                float scale = Mathf.Min(1.30f * aspect / bounds.size.x, .032f * lineCount / bounds.size.y);
                foreground.transform.localScale = shadow.transform.localScale = Vector3.one * scale;
            }
        }
        foreground.color = new Color(1, 1, 1, opacity);
        shadow.color = new Color(.015f, .025f, .035f, opacity * .9f);
    }

    private Camera? FindLocalCamera()
    {
        try
        {
            if (playerInstance == null)
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.GetName().Name != "Assembly-CSharp") continue;
                    Type? type = assembly.GetType("GorillaLocomotion.GTPlayer");
                    if (type == null) continue;
                    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;
                    playerInstance = (MemberInfo?)type.GetProperty("Instance", flags) ?? type.GetField("_instance", flags);
                    mainCamera = type.GetField("mainCamera", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }
            }
            object? player = Read(playerInstance, null);
            return player == null ? null : Read(mainCamera, player) as Camera;
        }
        catch { return null; }
    }

    private void EnsureText()
    {
        font = TryFont("LegacyRuntime.ttf") ?? TryFont("Arial.ttf");
        if (font == null) return;
        root = new GameObject("Hey Jace local chat notification");
        UnityEngine.Object.DontDestroyOnLoad(root);
        shadowMaterial = new Material(font.material) { name = "Hey Jace chat shadow", renderQueue = 4000 };
        textMaterial = new Material(font.material) { name = "Hey Jace chat text", renderQueue = 4001 };
        SetOverlay(shadowMaterial); SetOverlay(textMaterial);
        shadow = MakeText("Jace text shadow", shadowMaterial, false, new Vector3(.0015f, -.0018f, 0));
        foreground = MakeText("Jace text", textMaterial, true, new Vector3(0, 0, -.001f));
        root.SetActive(false);
        textDirty = true;
    }

    private TextMesh MakeText(string name, Material material, bool richText, Vector3 position)
    {
        var child = new GameObject(name);
        child.transform.SetParent(root!.transform, false);
        child.transform.localPosition = position;
        TextMesh label = child.AddComponent<TextMesh>();
        label.font = font; label.fontSize = 64; label.fontStyle = FontStyle.Bold; label.characterSize = 1;
        label.anchor = TextAnchor.UpperLeft; label.alignment = TextAlignment.Left;
        label.richText = richText;
        MeshRenderer renderer = child.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        child.transform.localScale = Vector3.one * .001f;
        return label;
    }

    private void EnsureSound()
    {
        if (audio != null) return;
        var sound = new GameObject("Hey Jace activation sound");
        UnityEngine.Object.DontDestroyOnLoad(sound);
        audio = sound.AddComponent<AudioSource>(); audio.playOnAwake = false; audio.spatialBlend = 0; audio.ignoreListenerPause = true;
        const int rate = 22050, length = 6615;
        var samples = new float[length];
        for (int i = 0; i < length; i++)
        {
            float time = i / (float)rate;
            float envelope = Mathf.Sin(Mathf.PI * i / (length - 1));
            float frequency = time < .13f ? 660 : 990;
            samples[i] = Mathf.Sin(2 * Mathf.PI * frequency * time) * envelope * .28f;
        }
        chime = AudioClip.Create("Jace two note activation", length, 1, rate, false);
        chime.SetData(samples, 0);
    }

    private static void SetOverlay(Material material)
    {
        if (material.shader.name == "UI/Default") material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        if (material.HasProperty("_ZTest")) material.SetInt("_ZTest", (int)CompareFunction.Always);
        if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 0);
    }

    private static Font? TryFont(string name) { try { return Resources.GetBuiltinResource<Font>(name); } catch { return null; } }
    private static object? Read(MemberInfo? member, object? owner) => member switch { FieldInfo field => field.GetValue(owner), PropertyInfo property => property.GetValue(owner, null), _ => null };

    private static string Wrap(string text, int width, out int lines)
    {
        var result = new StringBuilder();
        int column = 0;
        lines = 1;
        foreach (string word in text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (column > 0 && column + word.Length + 1 > width)
            {
                if (lines == 3) { result.Append("..."); break; }
                result.Append('\n'); column = 0; lines++;
            }
            else if (column > 0) { result.Append(' '); column++; }
            result.Append(word); column += word.Length;
        }
        return result.ToString();
    }

    public void Dispose()
    {
        ClearText();
        if (audio != null) UnityEngine.Object.Destroy(audio.gameObject);
        if (chime != null) UnityEngine.Object.Destroy(chime);
        audio = null; chime = null;
    }

    private void ClearText()
    {
        if (root != null) UnityEngine.Object.Destroy(root);
        if (textMaterial != null) UnityEngine.Object.Destroy(textMaterial);
        if (shadowMaterial != null) UnityEngine.Object.Destroy(shadowMaterial);
        root = null; foreground = shadow = null;
        textMaterial = shadowMaterial = null;
    }
}
