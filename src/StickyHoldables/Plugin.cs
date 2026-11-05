using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace StickyHoldables;

[BepInPlugin(Id, "Sticky Holdables", "1.0.0")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Id = "dev.local.stickyholdables";
    private static Plugin? active;
    private ConfigEntry<bool> sticky = null!;
    private ConfigEntry<KeyboardShortcut> toggleKey = null!;
    private HoldableApi? api;
    private Harmony? harmony;
    private readonly Queue<object> pendingAttachments = new();
    private bool rescan, unsupported, keyboardUnavailable;
    private float nextBind;

    private void Awake()
    {
        sticky = Config.Bind("General", "Enabled", true, "Keep equipped local holdables attached without holding grip.");
        toggleKey = Config.Bind("Controls", "ToggleKey", new KeyboardShortcut(KeyCode.F8), "Toggle sticky holdables. Disabling restores normal grip behavior.");
        sticky.SettingChanged += OnSettingChanged;
    }

    private void OnEnable()
    {
        active = this;
        unsupported = false;
        nextBind = 0;
    }

    private void Update()
    {
        if (!keyboardUnavailable)
        {
            try { if (toggleKey.Value.IsDown()) sticky.Value = !sticky.Value; }
            catch (Exception e)
            {
                keyboardUnavailable = true;
                Logger.LogWarning("Keyboard toggle unavailable; use the Enabled config setting and restart: " + e.Message);
            }
        }
        if (api == null && !unsupported && Time.unscaledTime >= nextBind)
        {
            nextBind = Time.unscaledTime + 2f;
            TryBind();
        }
    }

    private void TryBind()
    {
        Assembly? game = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
        if (game == null) return;
        try
        {
            var bound = new HoldableApi(game);
            var hooks = new Harmony(Id);
            harmony = hooks;
            api = bound;
            foreach (MethodInfo method in bound.ReleaseTargets)
                hooks.Patch(method, prefix: Patch(nameof(KeepOnRelease)));
            foreach (MethodInfo method in bound.EnableTargets)
                hooks.Patch(method, postfix: Patch(nameof(ItemEnabled)));
            foreach (MethodInfo method in bound.DisableTargets)
                hooks.Patch(method, postfix: Patch(nameof(ItemDisabled)));
            hooks.Patch(bound.HoldingQuery, prefix: Patch(nameof(HoldingQuery)));
            hooks.Patch(bound.CosmeticChange, prefix: Patch(nameof(BeforeCosmeticChange)), finalizer: Patch(nameof(AfterCosmeticChange)));
            bound.SetEnabled(sticky.Value);
            rescan = true;
            Logger.LogInfo("Sticky Holdables 1.0.0 loaded. F8 toggles sticky/normal grip. Only local equipped cosmetics are changed.");
        }
        catch (Exception e) { Fail(e); }
    }

    private static HarmonyMethod Patch(string name) => new(typeof(Plugin).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic));

    private void LateUpdate()
    {
        if (api == null || !api.Enabled || api.Suspended || (!rescan && pendingAttachments.Count == 0)) return;
        try
        {
            if (rescan)
            {
                rescan = false;
                pendingAttachments.Clear();
                foreach (UnityEngine.Object item in UnityEngine.Object.FindObjectsOfType(api.ItemType))
                    if (item is Behaviour behaviour && behaviour && behaviour.isActiveAndEnabled) api.Attach(item);
            }
            while (pendingAttachments.Count > 0)
                if (pendingAttachments.Dequeue() is Behaviour item && item && item.isActiveAndEnabled) api.Attach(item);
        }
        catch (Exception e) { Fail(e); }
    }

    private void OnSettingChanged(object sender, EventArgs e)
    {
        if (api == null) return;
        try
        {
            api.SetEnabled(sticky.Value);
            rescan = sticky.Value;
            Logger.LogInfo(sticky.Value ? "Sticky holdables enabled." : "Normal grip behavior restored.");
        }
        catch (Exception error) { Fail(error); }
    }

    private void OnDisable()
    {
        if (active == this) active = null;
        try { api?.SetEnabled(false); }
        catch (Exception e) { Logger.LogWarning(e.Message); }
        finally
        {
            pendingAttachments.Clear();
            try { harmony?.UnpatchSelf(); }
            catch (Exception e) { Logger.LogWarning("Could not remove all hooks; disabled hooks will pass through normally: " + e.Message); }
            finally { harmony = null; api = null; }
        }
    }

    private void OnDestroy() => sticky.SettingChanged -= OnSettingChanged;

    private void Fail(Exception e)
    {
        Logger.LogError("Sticky Holdables disabled; this game version/item could not be handled. " + e);
        unsupported = true;
        OnDisable();
    }

    private static bool KeepOnRelease(object __instance, object[] __args, ref bool __result)
    {
        Plugin? plugin = active;
        try
        {
            if (plugin?.api?.ShouldKeep(__instance, __args[0], __args[1]) != true) return true;
            __result = false;
            return false;
        }
        catch (Exception e) { plugin?.Fail(e); return true; }
    }

    private static bool HoldingQuery(object __instance, object[] __args, ref bool __result)
    {
        Plugin? plugin = active;
        try
        {
            if (plugin?.api?.IgnoreHoldingQuery(__instance, __args[0]) != true) return true;
            __result = false;
            return false;
        }
        catch (Exception e) { plugin?.Fail(e); return true; }
    }

    private static void ItemEnabled(object __instance)
    {
        if (active?.api?.Enabled == true) active.pendingAttachments.Enqueue(__instance);
    }
    private static void ItemDisabled(object __instance) => active?.api?.Forget(__instance);

    private static void BeforeCosmeticChange(object __instance, out bool __state)
    {
        __state = false;
        Plugin? plugin = active;
        try { __state = plugin?.api?.BeginCosmeticChange(__instance) == true; }
        catch (Exception e) { plugin?.Fail(e); }
    }

    // A finalizer also runs if the game's cosmetic update throws; never leave
    // dropping permanently suspended or swallow the game's original exception.
    private static Exception? AfterCosmeticChange(Exception? __exception, bool __state)
    {
        active?.api?.EndCosmeticChange(__state);
        if (__state && active != null) active.rescan = true;
        return __exception;
    }
}
