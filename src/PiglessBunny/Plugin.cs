using System;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace PiglessBunny;

[BepInPlugin("dev.local.piglessbunny", "Pigless Bunny", "1.0.0")]
public sealed class Plugin : BaseUnityPlugin
{
    private ConfigEntry<bool> show = null!, autoHand = null!, autoShoulder = null!;
    private ConfigEntry<float> size = null!;
    private ConfigEntry<KeyboardShortcut> cycle = null!, toggle = null!;
    private BunnyController? bunny;
    private bool keyboardUnavailable;
    private float retryAfter;
    private string lastStatus = "";

    private void Awake()
    {
        show = Config.Bind("General", "Enabled", true, "Show your local bunny companion.");
        size = Config.Bind("Appearance", "Size", .9f, new ConfigDescription("Bunny size relative to the local player's scale.", new AcceptableValueRange<float>(.5f, 1.5f)));
        autoHand = Config.Bind("Behavior", "AutoHandPerch", true, "Bunny hops onto a nearby still open hand after a short pause. Grip pickup always remains available.");
        autoShoulder = Config.Bind("Behavior", "AutoShoulderPerch", true, "Bunny joins your shoulder after you stand still for six seconds.");
        cycle = Config.Bind("Controls", "FollowShoulderKey", new KeyboardShortcut(KeyCode.F6), "Switch between following and sitting on your right shoulder.");
        toggle = Config.Bind("Controls", "ShowHideKey", new KeyboardShortcut(KeyCode.F7), "Show or hide the bunny.");
        Logger.LogInfo("Pigless Bunny 1.0.0 loaded. F6 follow/shoulder; F7 show/hide. Grip near bunny to carry; release to set down. Local cosmetic only.");
    }

    private void OnEnable() { bunny = new BunnyController(); retryAfter = 0; }

    private void Update()
    {
        if (keyboardUnavailable) return;
        try
        {
            if (toggle.Value.IsDown()) show.Value = !show.Value;
            if (cycle.Value.IsDown() && show.Value) bunny?.Cycle(Time.unscaledTime);
        }
        catch (Exception e)
        {
            keyboardUnavailable = true;
            Logger.LogWarning("Keyboard controls unavailable. Settings remain available in dev.local.piglessbunny.cfg: " + e.Message);
        }
    }

    private void LateUpdate()
    {
        if (bunny == null || Time.unscaledTime < retryAfter) return;
        try
        {
            float petSize = float.IsNaN(size.Value) || float.IsInfinity(size.Value) ? .9f : Mathf.Clamp(size.Value, .5f, 1.5f);
            bunny.Tick(Time.unscaledDeltaTime, Time.unscaledTime, show.Value, petSize, autoHand.Value, autoShoulder.Value);
            if (bunny.Status != lastStatus) { lastStatus = bunny.Status; Logger.LogInfo(lastStatus); }
        }
        catch (Exception e)
        {
            bunny.HideAfterError();
            retryAfter = Time.unscaledTime + 5;
            Logger.LogWarning("Bunny hidden while its local player/material APIs recover: " + e);
        }
    }

    private void OnDisable() { bunny?.Dispose(); bunny = null; }
    private void OnDestroy() => OnDisable();
}
