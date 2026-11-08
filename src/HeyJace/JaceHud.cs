using System;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace HeyJace;

internal sealed class JaceHud : IDisposable
{
    private string title = "HEY JACE", message = "", lastText = "";
    private float until;
    private GameObject? root;
    private Mesh? panelMesh;
    private Material? panelMaterial, textMaterial;
    private TextMesh? label;
    private Font? font;
    private AudioSource? audio;
    private AudioClip? chime;
    private Camera? camera;
    private MemberInfo? playerInstance, mainCamera;
    private GUIStyle? headingStyle, bodyStyle;
    private int fitFrames;
    private float nextCameraCheck;
    private float nextPanelAttempt;

    internal void Show(string heading, string text, float seconds = 6)
    {
        title = heading;
        message = text.Length > 180 ? text.Substring(0, 177) + "..." : text;
        until = Time.unscaledTime + Mathf.Clamp(seconds, 2, 15);
        lastText = "";
    }

    internal void Wake(bool playSound)
    {
        Show("JACE • LISTENING", "What's up? Say a command.", 8);
        if (playSound)
        {
            EnsureSound();
            audio?.PlayOneShot(chime, .42f);
        }
    }

    internal void Tick()
    {
        if (camera == null || Time.unscaledTime >= nextCameraCheck)
        {
            nextCameraCheck = Time.unscaledTime + 1;
            camera = FindLocalCamera();
        }
        if (root == null && Time.unscaledTime >= nextPanelAttempt)
        {
            nextPanelAttempt = Time.unscaledTime + 5;
            try { EnsurePanel(); }
            catch { ClearPanel(); throw; }
        }
        bool visible = Time.unscaledTime <= until && camera != null;
        root?.SetActive(visible);
        if (!visible || root == null || label == null || camera == null) return;
        float distance = Mathf.Max(.75f, camera.nearClipPlane * 4);
        root.transform.SetPositionAndRotation(camera.transform.TransformPoint(new Vector3(-.28f, .19f, 1) * distance), camera.transform.rotation);
        root.transform.localScale = Vector3.one * distance;
        string text = title + "\n" + Wrap(message, 40);
        if (text != lastText)
        {
            font?.RequestCharactersInTexture(text, 64, FontStyle.Normal);
            label.text = text; lastText = text; fitFrames = 3;
        }
        if (textMaterial != null && font != null) textMaterial.mainTexture = font.material.mainTexture;
        if (fitFrames > 0)
        {
            fitFrames--;
            Bounds bounds = label.GetComponent<Renderer>().localBounds;
            if (bounds.size.x > .001f && bounds.size.y > .001f)
                label.transform.localScale = Vector3.one * Mathf.Min(.34f / bounds.size.x, .078f / bounds.size.y);
        }
    }

    internal void DrawDesktop()
    {
        if (Time.unscaledTime > until) return;
        headingStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, richText = false, normal = { textColor = new Color(.35f, .9f, 1) } };
        bodyStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, richText = false, normal = { textColor = Color.white } };
        Color previous = GUI.color;
        try
        {
            GUI.color = new Color(.025f, .05f, .075f, .94f);
            GUI.DrawTexture(new Rect(22, 22, 350, 104), Texture2D.whiteTexture);
            GUI.color = new Color(.2f, .85f, 1);
            GUI.DrawTexture(new Rect(22, 22, 3, 104), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(36, 30, 324, 25), title, headingStyle);
            GUI.Label(new Rect(36, 59, 324, 62), message, bodyStyle);
        }
        finally { GUI.color = previous; }
    }

    private Camera? FindLocalCamera()
    {
        try
        {
            if (playerInstance == null)
            {
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name != "Assembly-CSharp") continue;
                    Type? t = a.GetType("GorillaLocomotion.GTPlayer");
                    if (t == null) continue;
                    playerInstance = (MemberInfo?)t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static) ?? t.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                    mainCamera = t.GetField("mainCamera", BindingFlags.Public | BindingFlags.Instance);
                }
            }
            object? player = Read(playerInstance, null);
            return player == null ? null : Read(mainCamera, player) as Camera;
        }
        catch { return null; }
    }

    private void EnsurePanel()
    {
        font = TryFont("LegacyRuntime.ttf") ?? TryFont("Arial.ttf");
        if (font == null) return;
        Shader? shader = Shader.Find("Unlit/Color") ?? Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) return;
        root = new GameObject("Hey Jace local popup");
        UnityEngine.Object.DontDestroyOnLoad(root);
        panelMesh = new Mesh { name = "Hey Jace popup panel" };
        panelMesh.vertices = new[] { new Vector3(-.18f, -.055f, 0), new Vector3(.18f, -.055f, 0), new Vector3(.18f, .055f, 0), new Vector3(-.18f, .055f, 0) };
        panelMesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 };
        panelMesh.RecalculateNormals(); panelMesh.RecalculateBounds();
        root.AddComponent<MeshFilter>().sharedMesh = panelMesh;
        panelMaterial = new Material(shader) { name = "Hey Jace popup background", renderQueue = 4000 };
        Color color = new(.025f, .05f, .075f, 1);
        if (panelMaterial.HasProperty("_Color")) panelMaterial.SetColor("_Color", color);
        if (panelMaterial.HasProperty("_BaseColor")) panelMaterial.SetColor("_BaseColor", color);
        SetOverlay(panelMaterial);
        MeshRenderer renderer = root.AddComponent<MeshRenderer>(); renderer.sharedMaterial = panelMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        var text = new GameObject("Jace popup text"); text.transform.SetParent(root.transform, false);
        text.transform.localPosition = new Vector3(-.168f, .043f, -.002f);
        label = text.AddComponent<TextMesh>(); label.font = font; label.fontSize = 64; label.characterSize = 1;
        label.color = new Color(.65f, .95f, 1); label.anchor = TextAnchor.UpperLeft; label.alignment = TextAlignment.Left; label.richText = false;
        text.transform.localScale = Vector3.one * .01f;
        textMaterial = new Material(font.material) { name = "Hey Jace popup font", renderQueue = 4001 };
        SetOverlay(textMaterial); text.GetComponent<Renderer>().sharedMaterial = textMaterial;
        root.SetActive(false);
    }

    private void EnsureSound()
    {
        if (audio != null) return;
        // A separate persistent audio object lets the activation chime finish
        // when the visual panel expires or its camera is being initialized.
        var sound = new GameObject("Hey Jace activation sound");
        UnityEngine.Object.DontDestroyOnLoad(sound);
        audio = sound.AddComponent<AudioSource>(); audio.playOnAwake = false; audio.spatialBlend = 0; audio.ignoreListenerPause = true;
        const int rate = 22050, length = 6615;
        var samples = new float[length];
        for (int i = 0; i < length; i++)
        {
            float t = i / (float)rate;
            float envelope = Mathf.Sin(Mathf.PI * i / (length - 1));
            float hz = t < .13f ? 660 : 990;
            samples[i] = Mathf.Sin(2 * Mathf.PI * hz * t) * envelope * .28f;
        }
        chime = AudioClip.Create("Jace two note activation", length, 1, rate, false); chime.SetData(samples, 0);
    }

    private static void SetOverlay(Material material)
    {
        if (material.HasProperty("_ZTest")) material.SetInt("_ZTest", (int)CompareFunction.Always);
        if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 0);
    }
    private static Font? TryFont(string name) { try { return Resources.GetBuiltinResource<Font>(name); } catch { return null; } }
    private static object? Read(MemberInfo? member, object? owner) => member switch { FieldInfo f => f.GetValue(owner), PropertyInfo p => p.GetValue(owner, null), _ => null };
    private static string Wrap(string text, int width)
    {
        var result = new StringBuilder(); int column = 0;
        foreach (string word in text.Split(' '))
        {
            if (column + word.Length > width) { result.Append('\n'); column = 0; }
            else if (column > 0) { result.Append(' '); column++; }
            result.Append(word); column += word.Length;
        }
        return result.ToString();
    }

    public void Dispose()
    {
        ClearPanel();
        if (audio != null) UnityEngine.Object.Destroy(audio.gameObject);
        if (chime != null) UnityEngine.Object.Destroy(chime);
        audio = null; chime = null;
    }

    private void ClearPanel()
    {
        if (root != null) UnityEngine.Object.Destroy(root);
        if (panelMesh != null) UnityEngine.Object.Destroy(panelMesh);
        if (panelMaterial != null) UnityEngine.Object.Destroy(panelMaterial);
        if (textMaterial != null) UnityEngine.Object.Destroy(textMaterial);
        root = null; label = null; panelMesh = null; panelMaterial = textMaterial = null;
    }
}
