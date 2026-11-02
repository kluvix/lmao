using System;
using System.Reflection;
using UnityEngine;
namespace Pigless.Sheesh;

// Uses the supplied game's local lighting API; no room RPC or network state changes.
internal sealed class TimeOfDayTool : IDisposable
{
    private UnityEngine.Object manager;
    private MethodInfo setTime, update;
    private FieldInfo setting;
    private object originalSetting;
    private int originalIndex;
    private int count;
    private string[] names;
    private int selected = -1;
    internal string Status { get; private set; } = "GAME DEFAULT";
    internal bool Active => selected >= 0;

    internal void Cycle()
    {
        if (manager == null && !Bind()) return;
        int next = selected + 1;
        if (next >= count) { Dispose(); return; }
        setTime.Invoke(manager, new object[] { next, true });
        selected = next;
        Status = names != null && next < names.Length ? names[next].ToUpperInvariant() : "PRESET " + (next + 1);
    }

    private bool Bind()
    {
        var type = typeof(GorillaTagger).Assembly.GetType("BetterDayNightManager");
        if (type == null) { Status = "API UNAVAILABLE"; return false; }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        var candidate = type.GetField("instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as UnityEngine.Object;
        if (candidate == null) { Status = "WAITING FOR MAP"; return false; }
        var method = type.GetMethod("SetTimeOfDay", flags, null, new[] { typeof(int), typeof(bool) }, null);
        var updater = type.GetMethod("UpdateTimeOfDay", flags, null, new[] { typeof(bool) }, null);
        var index = type.GetField("currentTimeIndex", flags);
        var ranges = type.GetProperty("timeOfDayRange", flags)?.GetValue(candidate) as Array;
        var mode = type.GetField("currentSetting", flags);
        if (method == null || updater == null || mode == null || index?.GetValue(candidate) is not int current || ranges == null || ranges.Length == 0 || current < 0 || current >= ranges.Length)
        { Status = "API UNAVAILABLE"; return false; }
        manager = candidate; setTime = method; update = updater; setting = mode;
        originalSetting = mode.GetValue(candidate); originalIndex = current; count = ranges.Length;
        names = type.GetField("dayNightLightmapNames", flags)?.GetValue(candidate) as string[];
        selected = -1;
        return true;
    }

    public void Dispose()
    {
        try
        {
            if (manager != null && selected >= 0)
            {
                setTime.Invoke(manager, new object[] { originalIndex, true });
                setting.SetValue(manager, originalSetting);
                update.Invoke(manager, new object[] { true });
            }
        }
        finally { selected = -1; manager = null; Status = "GAME DEFAULT"; }
    }
}
