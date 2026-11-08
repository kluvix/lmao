using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace PiglessBunny
{
    /// <summary>A small original, local-only rabbit assembled from owned low-poly geometry.</summary>
    internal sealed class BunnyModel : IDisposable
    {
        private static readonly Vector3 BodyPosition = new Vector3(0f, 0.116f, 0f);
        private static readonly Vector3 HeadPosition = new Vector3(0f, 0.235f, 0.018f);
        private static readonly Vector3 TailPosition = new Vector3(0f, -0.018f, -0.074f);

        private readonly List<Material> materials = new List<Material>(7);
        private readonly List<Texture2D> textures = new List<Texture2D>(7);
        private GameObject rootObject;
        private Mesh roundedMesh;
        private Mesh boxMesh;
        private Texture2D? sharedFurTexture;
        private Transform body;
        private Transform head;
        private Transform leftEar;
        private Transform rightEar;
        private Transform leftEye;
        private Transform rightEye;
        private Transform tail;
        private bool disposed;

        public Transform Root { get; }

        public BunnyModel()
        {
            try
            {
                rootObject = new GameObject("Pigless Bunny (local pet)");
                rootObject.SetActive(false);
                Root = rootObject.transform;
                Object.DontDestroyOnLoad(rootObject);

                roundedMesh = new Mesh { name = "Pigless Bunny Faceted Mesh" };
                BuildRoundedMesh(roundedMesh);
                boxMesh = new Mesh { name = "Pigless Bunny Pixel Detail Mesh" };
                BuildBoxMesh(boxMesh);

                Shader shader = FindTexturedShader();
                Material fur = CreateMaterial(shader, "Chestnut fur", new Color(0.43f, 0.285f, 0.19f));
                Material darkFur = CreateMaterial(shader, "Dark chestnut", new Color(0.32f, 0.20f, 0.145f));
                Material cream = CreateMaterial(shader, "Warm cream", new Color(0.88f, 0.79f, 0.65f));
                Material pink = CreateMaterial(shader, "Dusty pink", new Color(0.78f, 0.48f, 0.49f));
                Material nosePink = CreateMaterial(shader, "Rose nose", new Color(0.66f, 0.34f, 0.36f));
                Material dark = CreateMaterial(shader, "Face details", new Color(0.10f, 0.07f, 0.055f));

                body = Node("Body pivot", Root, BodyPosition);
                Rounded("Body", body, Vector3.zero, new Vector3(0.145f, 0.18f, 0.12f), fur);
                Rounded("Cream belly", body, new Vector3(0f, -0.004f, 0.052f), new Vector3(0.096f, 0.125f, 0.029f), cream);
                Rounded("Chest", body, new Vector3(0f, 0.049f, 0.041f), new Vector3(0.078f, 0.048f, 0.032f), cream);

                Transform leftArm = Rounded("Left front paw", body, new Vector3(-0.064f, 0.003f, 0.031f), new Vector3(0.041f, 0.072f, 0.045f), fur);
                Transform rightArm = Rounded("Right front paw", body, new Vector3(0.064f, 0.003f, 0.031f), new Vector3(0.041f, 0.072f, 0.045f), fur);
                leftArm.localRotation = Quaternion.Euler(0f, 0f, -14f);
                rightArm.localRotation = Quaternion.Euler(0f, 0f, 14f);

                Rounded("Left hind paw", Root, new Vector3(-0.046f, 0.023f, 0.033f), new Vector3(0.068f, 0.046f, 0.086f), darkFur);
                Rounded("Right hind paw", Root, new Vector3(0.046f, 0.023f, 0.033f), new Vector3(0.068f, 0.046f, 0.086f), darkFur);
                Rounded("Left paw pad", Root, new Vector3(-0.046f, 0.030f, 0.069f), new Vector3(0.035f, 0.015f, 0.009f), pink);
                Rounded("Right paw pad", Root, new Vector3(0.046f, 0.030f, 0.069f), new Vector3(0.035f, 0.015f, 0.009f), pink);

                tail = Node("Tail pivot", body, TailPosition);
                Rounded("Tail", tail, Vector3.zero, new Vector3(0.060f, 0.059f, 0.062f), cream);
                Rounded("Tail tuft", tail, new Vector3(0.013f, 0.014f, -0.010f), new Vector3(0.036f, 0.039f, 0.036f), cream);

                head = Node("Head pivot", Root, HeadPosition);
                Rounded("Head", head, Vector3.zero, new Vector3(0.146f, 0.127f, 0.12f), fur);
                Rounded("Left muzzle", head, new Vector3(-0.017f, -0.025f, 0.047f), new Vector3(0.044f, 0.033f, 0.030f), cream);
                Rounded("Right muzzle", head, new Vector3(0.017f, -0.025f, 0.047f), new Vector3(0.044f, 0.033f, 0.030f), cream);

                Transform leftCheek = Rounded("Left pink cheek", head, new Vector3(-0.050f, -0.014f, 0.040f), new Vector3(0.022f, 0.013f, 0.009f), pink);
                Transform rightCheek = Rounded("Right pink cheek", head, new Vector3(0.050f, -0.014f, 0.040f), new Vector3(0.022f, 0.013f, 0.009f), pink);
                leftCheek.localRotation = Quaternion.Euler(0f, -30f, 0f);
                rightCheek.localRotation = Quaternion.Euler(0f, 30f, 0f);

                Box("Nose", head, new Vector3(0f, -0.019f, 0.0585f), new Vector3(0.014f, 0.010f, 0.009f), nosePink);
                Box("Mouth stem", head, new Vector3(0f, -0.029f, 0.054f), new Vector3(0.003f, 0.011f, 0.003f), dark);
                Transform leftSmile = Box("Left mouth", head, new Vector3(-0.004f, -0.034f, 0.056f), new Vector3(0.010f, 0.0025f, 0.003f), dark);
                Transform rightSmile = Box("Right mouth", head, new Vector3(0.004f, -0.034f, 0.056f), new Vector3(0.010f, 0.0025f, 0.003f), dark);
                leftSmile.localRotation = Quaternion.Euler(0f, 0f, -20f);
                rightSmile.localRotation = Quaternion.Euler(0f, 0f, 20f);

                leftEar = CreateEar("Left ear", head, -0.040f, 12f, fur, pink);
                rightEar = CreateEar("Right ear", head, 0.040f, -12f, fur, pink);
                leftEye = CreateEye("Left eye", head, -0.032f, -20f, dark);
                rightEye = CreateEye("Right eye", head, 0.032f, 20f, dark);
                Animate(0f, 0f, 1f);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void SetVisible(bool visible)
        {
            if (!disposed && rootObject != null)
                rootObject.SetActive(visible);
        }

        /// <summary>Animates only model-local pivots; the controller owns the root's pose.</summary>
        public void Animate(float time, float hop01, float idleAmount)
        {
            if (disposed || rootObject == null)
                return;

            float hop = Mathf.Clamp01(hop01);
            float idle = Mathf.Clamp01(idleAmount);
            float breath = Mathf.Sin(time * 2.4f) * Mathf.Lerp(0.25f, 1f, idle);
            body.localScale = new Vector3(1f + breath * 0.012f - hop * 0.025f,
                1f + breath * 0.020f + hop * 0.035f, 1f + breath * 0.012f);
            head.localPosition = HeadPosition + new Vector3(0f, breath * 0.001f, 0f);
            head.localRotation = Quaternion.Euler(Mathf.Sin(time * 1.3f) * idle * 2f - hop * 3f,
                Mathf.Sin(time * 0.7f) * idle * 4f, Mathf.Sin(time * 1.1f) * idle * 1.5f);
            leftEar.localRotation = Quaternion.Euler(3f + Mathf.Sin(time * 2.1f) * 3f - hop * 11f,
                0f, 12f + Mathf.Sin(time * 1.9f) * 3f);
            rightEar.localRotation = Quaternion.Euler(3f + Mathf.Sin(time * 2.1f + 0.8f) * 3f - hop * 9f,
                0f, -12f + Mathf.Sin(time * 1.7f + 1.1f) * 3f);
            tail.localRotation = Quaternion.Euler(0f, Mathf.Sin(time * 6.7f) * (7f + idle * 6f),
                Mathf.Sin(time * 5.1f) * 9f);
            tail.localPosition = TailPosition + new Vector3(Mathf.Sin(time * 5.1f) * 0.002f, 0f, 0f);

            // A short blink every few seconds, rather than continuously pulsing eyes.
            float blinkPhase = Mathf.Repeat(time + 1.7f, 5.8f);
            float blink = blinkPhase < 0.18f ? Mathf.Sin(blinkPhase / 0.18f * Mathf.PI) : 0f;
            Vector3 eyeScale = new Vector3(1f, 1f - blink * 0.90f, 1f);
            leftEye.localScale = eyeScale;
            rightEye.localScale = eyeScale;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;

            if (rootObject != null)
            {
                rootObject.SetActive(false);
                Object.Destroy(rootObject);
            }
            if (roundedMesh != null)
                Object.Destroy(roundedMesh);
            if (boxMesh != null)
                Object.Destroy(boxMesh);
            for (int i = 0; i < materials.Count; ++i)
                if (materials[i] != null)
                    Object.Destroy(materials[i]);
            for (int i = 0; i < textures.Count; ++i)
                if (textures[i] != null)
                    Object.Destroy(textures[i]);
            materials.Clear();
            textures.Clear();
        }

        private Transform CreateEar(string name, Transform parent, float x, float angle, Material fur, Material pink)
        {
            Transform pivot = Node(name, parent, new Vector3(x, 0.043f, -0.004f));
            pivot.localRotation = Quaternion.Euler(3f, 0f, angle);
            Box(name + " outer", pivot, new Vector3(0f, 0.055f, 0f), new Vector3(0.039f, 0.134f, 0.032f), fur);
            Box(name + " pink inner", pivot, new Vector3(0f, 0.056f, 0.016f), new Vector3(0.020f, 0.095f, 0.005f), pink);
            return pivot;
        }

        private Transform CreateEye(string name, Transform parent, float x, float angle, Material dark)
        {
            Transform pivot = Node(name + " blink pivot", parent, new Vector3(x, 0.013f, 0.049f));
            pivot.localRotation = Quaternion.Euler(0f, angle, 0f);
            Box(name, pivot, Vector3.zero, new Vector3(0.014f, 0.018f, 0.008f), dark);
            return pivot;
        }

        private static Transform Node(string name, Transform parent, Vector3 position)
        {
            Transform transform = new GameObject(name).transform;
            transform.SetParent(parent, false);
            transform.localPosition = position;
            return transform;
        }

        private Transform Rounded(string name, Transform parent, Vector3 position, Vector3 size, Material material)
        {
            return Part(name, parent, position, size, material, roundedMesh);
        }

        private Transform Box(string name, Transform parent, Vector3 position, Vector3 size, Material material)
        {
            return Part(name, parent, position, size, material, boxMesh);
        }

        private static Transform Part(string name, Transform parent, Vector3 position, Vector3 size, Material material, Mesh mesh)
        {
            Transform transform = Node(name, parent, position);
            transform.localScale = size;
            MeshFilter filter = transform.gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = transform.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return transform;
        }

        private static Shader FindTexturedShader()
        {
            string[] names = { "GorillaTag/UberShader", "Universal Render Pipeline/Unlit", "Unlit/Texture", "Standard" };
            foreach (string name in names)
            {
                Shader shader = Shader.Find(name);
                if (shader == null || !shader.isSupported)
                    continue;
                Material? probe = null;
                try
                {
                    probe = new Material(shader);
                    bool textured = probe.HasProperty("_MainTex") || probe.HasProperty("_BaseMap");
                    bool tinted = probe.HasProperty("_Color") || probe.HasProperty("_BaseColor");
                    if (textured && (tinted || name == "Unlit/Texture"))
                        return shader;
                }
                finally
                {
                    if (probe != null)
                        Object.Destroy(probe);
                }
            }
            throw new InvalidOperationException("PiglessBunny needs a supported textured GorillaTag, URP Unlit, Unlit/Texture, or Standard shader.");
        }

        private Material CreateMaterial(Shader shader, string name, Color tint)
        {
            Material material = new Material(shader) { name = "Pigless Bunny " + name };
            materials.Add(material);
            bool tinted = material.HasProperty("_BaseColor") || material.HasProperty("_Color");
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", tint);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", tint);

            Texture2D texture;
            if (tinted)
            {
                if (sharedFurTexture == null)
                    sharedFurTexture = CreateFurTexture(Color.white, "Fur pixels");
                texture = sharedFurTexture;
            }
            else
            {
                // Built-in Unlit/Texture has no color property: bake the tint into owned pixels.
                texture = CreateFurTexture(tint, name + " pixels");
            }
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", 0f);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0f);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_SpecularHighlights"))
                material.SetFloat("_SpecularHighlights", 0f);
            if (material.HasProperty("_GlossyReflections"))
                material.SetFloat("_GlossyReflections", 0f);
            return material;
        }

        private Texture2D CreateFurTexture(Color tint, string name)
        {
            const int size = 32;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            textures.Add(texture);
            texture.name = "Pigless Bunny " + name;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.anisoLevel = 0;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; ++y)
            {
                for (int x = 0; x < size; ++x)
                {
                    // Fixed integer hash: no Unity random state, external art, or runtime downloads.
                    uint hash = unchecked((uint)(x * 374761393 + y * 668265263));
                    hash = (hash ^ (hash >> 13)) * 1274126177u;
                    hash ^= hash >> 16;
                    float shade = 0.91f + (hash & 7u) * 0.012f;
                    if ((hash & 63u) == 0u)
                        shade = 0.79f;
                    pixels[y * size + x] = new Color(tint.r * shade, tint.g * shade, tint.b * shade, 1f);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static void BuildRoundedMesh(Mesh mesh)
        {
            const int segments = 8;
            const int rings = 6;
            List<Vector3> vertices = new List<Vector3>(240);
            List<Vector3> normals = new List<Vector3>(240);
            List<Vector2> uv = new List<Vector2>(240);
            List<int> triangles = new List<int>(240);
            for (int ring = 0; ring < rings; ++ring)
            {
                float v0 = ring / (float)rings;
                float v1 = (ring + 1) / (float)rings;
                for (int segment = 0; segment < segments; ++segment)
                {
                    float u0 = segment / (float)segments;
                    float u1 = (segment + 1) / (float)segments;
                    Vector3 a = SpherePoint(u0, v0);
                    Vector3 b = SpherePoint(u1, v0);
                    Vector3 c = SpherePoint(u0, v1);
                    Vector3 d = SpherePoint(u1, v1);
                    if (ring != 0)
                        AddTriangle(a, b, c, new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u0, v1), vertices, normals, uv, triangles);
                    if (ring != rings - 1)
                        AddTriangle(b, d, c, new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1), vertices, normals, uv, triangles);
                }
            }
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            SetWhiteVertexColors(mesh, vertices.Count);
            mesh.RecalculateBounds();
        }

        private static Vector3 SpherePoint(float u, float v)
        {
            float phi = v * Mathf.PI;
            float theta = u * Mathf.PI * 2f;
            float radius = Mathf.Sin(phi) * 0.5f;
            return new Vector3(radius * Mathf.Cos(theta), Mathf.Cos(phi) * 0.5f, radius * Mathf.Sin(theta));
        }

        private static void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc,
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uv, List<int> triangles)
        {
            int first = vertices.Count;
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            normals.Add(normal); normals.Add(normal); normals.Add(normal);
            uv.Add(ua); uv.Add(ub); uv.Add(uc);
            triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
        }

        private static void BuildBoxMesh(Mesh mesh)
        {
            List<Vector3> vertices = new List<Vector3>(36);
            List<Vector3> normals = new List<Vector3>(36);
            List<Vector2> uv = new List<Vector2>(36);
            List<int> triangles = new List<int>(36);
            Vector3[] corners =
            {
                new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f),
                new Vector3(0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f),
                new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(-0.5f, -0.5f, 0.5f)
            };
            for (int face = 0; face < 6; ++face)
            {
                int i = face * 4;
                AddTriangle(corners[i], corners[i + 1], corners[i + 2], new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), vertices, normals, uv, triangles);
                AddTriangle(corners[i], corners[i + 2], corners[i + 3], new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f), vertices, normals, uv, triangles);
            }
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            SetWhiteVertexColors(mesh, vertices.Count);
            mesh.RecalculateBounds();
        }

        private static void SetWhiteVertexColors(Mesh mesh, int count)
        {
            // Native game shaders may multiply tint/texture by vertex color.
            // Explicit white keeps custom meshes from inheriting an undefined attribute.
            Color32[] colors = new Color32[count];
            for (int i = 0; i < count; i++) colors[i] = new Color32(255, 255, 255, 255);
            mesh.colors32 = colors;
        }
    }
}
