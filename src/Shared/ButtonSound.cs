using System;
using UnityEngine;
namespace Pigless.Interaction;

// Generated locally; no external audio files or game sound IDs required.
internal sealed class ButtonSound : IDisposable
{
    private GameObject root;
    private AudioSource source;
    private AudioClip clip;
    internal void Play(Vector3 position)
    {
        if (root == null)
        {
            root = new GameObject("Pigless button sound");
            UnityEngine.Object.DontDestroyOnLoad(root);
            source = root.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.minDistance = 0.25f;
            source.maxDistance = 4f;
            source.volume = 0.22f;
            source.ignoreListenerPause = true;
            const int rate = 22050;
            var samples = new float[1323];
            for (int i = 0; i < samples.Length; i++)
            {
                double t = (double)i / rate;
                double envelope = Math.Min(1.0, i / 64.0) * Math.Exp(-65.0 * t);
                samples[i] = (float)(Math.Sin(2 * Math.PI * 880 * t) * envelope);
            }
            clip = AudioClip.Create("Pigless click", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
        }
        root.transform.position = position;
        source.PlayOneShot(clip);
    }
    public void Dispose()
    {
        if (root != null) UnityEngine.Object.Destroy(root);
        if (clip != null) UnityEngine.Object.Destroy(clip);
        root = null; source = null; clip = null;
    }
}
