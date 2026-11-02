using System;
using System.IO;
using BepInEx;
using UnityEngine;
namespace Pigless.Sheesh;

// Local physics prop. Does not inject a network prefab or claim remote ownership.
internal sealed class JacesusPillow : IDisposable
{
    private GameObject root;
    private Rigidbody body;
    private BoxCollider collider;
    private Mesh mesh;
    private Material material;
    private Texture2D texture;
    private Transform heldBy;
    private bool previousLeft, previousRight;
    private Vector3 offset, lastPosition, velocity, angularVelocity;
    private Quaternion rotationOffset, lastRotation;
    internal string Status { get; private set; } = "SPAWN";
    internal bool Active => root != null;
    internal string ImagePath => Path.Combine(Paths.PluginPath, "Pigless", "assets", "jacesus.png");

    internal void Toggle(Transform head)
    {
        if (Active) { Dispose(); return; }
        if (head == null) { Status = "WAITING FOR PLAYER"; return; }
        if (!File.Exists(ImagePath)) { Status = "NEED JACESUS.PNG"; return; }
        try
        {
            texture = new Texture2D(2, 2);
            if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(ImagePath), false))
                throw new InvalidDataException("jacesus.png could not be decoded.");
            root = new GameObject("jacesus");
            UnityEngine.Object.DontDestroyOnLoad(root);
            root.transform.SetPositionAndRotation(head.position + head.forward * 0.8f, Quaternion.LookRotation(head.forward, Vector3.up));
            mesh = MakePillow();
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            Shader shader = Shader.Find("Unlit/Texture") ?? Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("No supported pillow shader.");
            material = new Material(shader) { mainTexture = texture };
            root.AddComponent<MeshRenderer>().sharedMaterial = material;
            collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.48f, 1.05f, 0.18f);
            body = root.AddComponent<Rigidbody>();
            body.mass = 0.65f; body.drag = 0.15f; body.angularDrag = 0.3f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            // Avoid a held prop driving the local locomotion colliders.
            var player = GorillaLocomotion.GTPlayer.Instance;
            var tagger = GorillaTagger.Instance;
            if ((UnityEngine.Object)(object)player != null)
                foreach (var own in ((Component)(object)player).GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(collider, own);
            if ((UnityEngine.Object)(object)tagger != null)
                foreach (var own in ((Component)(object)tagger).GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(collider, own);
            Status = "GRIP TO GRAB";
        }
        catch { Dispose(); Status = "SPAWN FAILED"; throw; }
    }

    internal void Tick(Transform left, Transform right, bool leftGrip, bool rightGrip, float dt)
    {
        bool leftEdge = leftGrip && !previousLeft, rightEdge = rightGrip && !previousRight;
        previousLeft = leftGrip; previousRight = rightGrip;
        if (root == null) return;
        if (heldBy == null)
        {
            Transform hand = leftEdge && Near(left) ? left : rightEdge && Near(right) ? right : null;
            if (hand == null) return;
            heldBy = hand;
            offset = hand.InverseTransformPoint(root.transform.position);
            rotationOffset = Quaternion.Inverse(hand.rotation) * root.transform.rotation;
            lastPosition = root.transform.position; lastRotation = root.transform.rotation;
            velocity = angularVelocity = Vector3.zero;
            body.isKinematic = true;
            Status = "HELD - RELEASE TO THROW";
        }
        bool holding = heldBy == left ? leftGrip : heldBy == right && rightGrip;
        if (!holding)
        {
            Release(); return;
        }
        Vector3 position = heldBy.TransformPoint(offset);
        Quaternion rotation = heldBy.rotation * rotationOffset;
        if (dt > 0.0001f)
        {
            velocity = Vector3.ClampMagnitude((position - lastPosition) / dt, 12f);
            Quaternion delta = rotation * Quaternion.Inverse(lastRotation);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            angularVelocity = axis.sqrMagnitude > 0.001f && !float.IsNaN(axis.x) && !float.IsInfinity(axis.x)
                ? Vector3.ClampMagnitude(axis * (angle * Mathf.Deg2Rad / dt), 20f) : Vector3.zero;
        }
        root.transform.SetPositionAndRotation(position, rotation);
        lastPosition = position; lastRotation = rotation;
    }

    private bool Near(Transform hand) => hand != null && Vector3.Distance(hand.position, collider.ClosestPoint(hand.position)) <= 0.12f;
    internal void Release()
    {
        if (body != null && heldBy != null)
        { body.isKinematic = false; body.velocity = velocity; body.angularVelocity = angularVelocity; }
        heldBy = null;
        if (root != null) Status = "GRIP TO GRAB";
    }

    private static Mesh MakePillow()
    {
        // Two curved rectangular faces, stitched around the edge; full photo on each face.
        const int nx = 12, ny = 24;
        var vertices = new Vector3[2 * (nx + 1) * (ny + 1)];
        var uv = new Vector2[vertices.Length];
        var triangles = new System.Collections.Generic.List<int>();
        int stride = nx + 1, face = stride * (ny + 1);
        for (int side = 0; side < 2; side++)
        for (int y = 0; y <= ny; y++)
        for (int x = 0; x <= nx; x++)
        {
            float u = (float)x / nx, v = (float)y / ny;
            float bulge = 0.015f + 0.075f * Mathf.Sin(u * Mathf.PI) * Mathf.Sin(v * Mathf.PI);
            int i = side * face + y * stride + x;
            vertices[i] = new Vector3((u - 0.5f) * 0.48f, (v - 0.5f) * 1.05f, (side == 0 ? -1 : 1) * bulge);
            uv[i] = new Vector2(side == 0 ? u : 1 - u, v);
            if (x < nx && y < ny)
            {
                if (side == 0) { triangles.AddRange(new[] { i, i + stride, i + 1, i + 1, i + stride, i + stride + 1 }); }
                else { triangles.AddRange(new[] { i, i + 1, i + stride, i + 1, i + stride + 1, i + stride }); }
            }
        }
        void Seam(int a, int b) => triangles.AddRange(new[] { a, b, a + face, b, b + face, a + face });
        for (int x = 0; x < nx; x++) { Seam(x, x + 1); Seam(ny * stride + x + 1, ny * stride + x); }
        for (int y = 0; y < ny; y++) { Seam((y + 1) * stride, y * stride); Seam(y * stride + nx, (y + 1) * stride + nx); }
        var result = new Mesh { name = "jacesus body pillow", vertices = vertices, uv = uv, triangles = triangles.ToArray() };
        result.RecalculateNormals(); result.RecalculateBounds(); return result;
    }

    public void Dispose()
    {
        heldBy = null;
        if (root != null) UnityEngine.Object.Destroy(root);
        if (mesh != null) UnityEngine.Object.Destroy(mesh);
        if (material != null) UnityEngine.Object.Destroy(material);
        if (texture != null) UnityEngine.Object.Destroy(texture);
        root = null; body = null; collider = null; mesh = null; material = null; texture = null;
        Status = "SPAWN";
    }
}
