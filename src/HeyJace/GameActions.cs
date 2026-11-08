using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using HarmonyLib;
using HeyJace.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HeyJace;

// Bind only to the game's already loaded assembly. The uploaded assembly was
// inspected as metadata; it is neither a reference nor loaded by this plugin.
internal sealed class GameActions : IDisposable
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
    private const string PatchId = "heyjace.local-time-player-join";
    private readonly int mainThread = Thread.CurrentThread.ManagedThreadId;
    private Assembly? game;
    private bool disposed;
    private Harmony? harmony;
    private TimeSnapshot? time;
    private static GameActions? timeGuardOwner;

    internal GameActions()
    {
        SceneManager.activeSceneChanged += ActiveSceneChanged;
        SceneManager.sceneLoaded += SceneLoaded;
        SceneManager.sceneUnloaded += SceneUnloaded;
    }

    internal ActionResult Execute(JaceCommand command)
    {
        if (!OnMainThread) return Failure("Game actions must run on the Unity main thread.");
        if (disposed) return Failure("Hey Jace is disabled.");
        if (command == null) return Failure("No command was supplied.");
        try
        {
            return command.Kind switch
            {
                CommandKind.JoinRoom => JoinRoom(command.Argument),
                CommandKind.SetColor => SetColor(command.Argument),
                CommandKind.SetTime => SetTime(command.Argument),
                CommandKind.Equip => ChangeCosmetic(command.Argument, true),
                CommandKind.Unequip => ChangeCosmetic(command.Argument, false),
                CommandKind.EquipOutfit => EquipOutfit(command.Argument),
                CommandKind.SetName => SetName(command.Argument),
                CommandKind.SetMic => SetMic(command.Argument),
                _ => Failure("That command is not supported.")
            };
        }
        catch (Exception error)
        {
            Exception cause = error.GetBaseException();
            string detail = cause.Message;
            if (detail.Length > 200) detail = detail.Substring(0, 200);
            return Failure("Could not " + Describe(command.Kind) + ": " + detail);
        }
    }

    internal void Tick()
    {
        if (!OnMainThread || disposed || time == null) return;
        // Room entry and the game's own time controls take priority. In
        // particular, never restore a previous room's time over a new host.
        try
        {
            object? current = Field(time.Manager.GetType(), "instance", Static).GetValue(null);
            if (!Alive(time.Manager) || !ReferenceEquals(current, time.Manager)) RestoreTime();
            else if (!string.Equals(time.Setting.GetValue(time.Manager)?.ToString(), "Static", StringComparison.Ordinal) ||
                     (int)time.LastSent.GetValue(time.Manager)! != time.SelectedIndex)
                time = null;
        }
        catch
        {
            RestoreTime();
        }
    }

    private ActionResult JoinRoom(string argument)
    {
        string room = (argument ?? "").Trim().ToUpperInvariant();
        if (room.Length < 1 || room.Length > 10) return Failure("Use a room code of 1 to 10 letters or digits.");
        foreach (char c in room)
            if (!(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9'))
                return Failure("Room codes can contain only letters and digits.");
        Type controllerType = GameType("GorillaNetworking.PhotonNetworkController");
        object controller = Singleton(controllerType, "Instance");
        Type joinType = GameType("GorillaNetworking.JoinType");
        MethodInfo join = Method(controllerType, "AttemptToJoinSpecificRoom", Instance, typeof(string), joinType);
        object solo = Enum.Parse(joinType, "Solo", false);
        RestoreTime();
        // This is the normal game join path; permission, deferral, room and
        // connection failures remain the game's responsibility.
        join.Invoke(controller, new[] { (object)room, solo });
        return Success("Requested room " + room + ".");
    }

    private ActionResult SetColor(string argument)
    {
        if (!ColorSpec.TryParse(argument, out ColorSpec color))
            return Failure("Say three Gorilla Tag color numbers from zero to nine, for example nine zero five.");
        Type cosmeticsType = GameType("GorillaNetworking.CosmeticsController");
        object controller = Singleton(cosmeticsType, "instance");
        object tagger = Singleton(GameType("GorillaTagger"), "Instance");
        RequireAlive(Field(tagger.GetType(), "offlineVRRig", Instance).GetValue(tagger), "Local player is not ready.");
        Singleton(GameType("GorillaNetworking.GorillaComputer"), "instance");
        object network = Singleton(GameType("NetworkSystem"), "Instance");
        if ((bool)Property(network.GetType(), "InRoom", Instance).GetValue(network)!)
            RequireAlive(Property(tagger.GetType(), "myVRRig", Instance).GetValue(tagger), "Network player is not ready.");
        // The normal saved-outfit color path updates tagger + computer, emits
        // OnPlayerColorSet, uses the normal color RPC, and saves RGB prefs.
        Method(cosmeticsType, "UpdateMonkeColor", Instance, typeof(Vector3), typeof(bool))
            .Invoke(controller, new object[] { new Vector3(color.R, color.G, color.B), true });
        return Success("Color set to " + color.Label + ".");
    }

    private ActionResult SetName(string argument)
    {
        string requested = (argument ?? "").Trim().ToUpperInvariant();
        if (requested.Length < 1 || requested.Length > 12)
            return Failure("Use a name of one to twelve letters or digits.");
        foreach (char character in requested)
            if (!(character >= 'A' && character <= 'Z') && !(character >= '0' && character <= '9'))
                return Failure("Names can contain only letters and digits.");
        Type type = GameType("GorillaNetworking.GorillaComputer");
        object computer = Singleton(type, "instance");
        if (!(bool)Property(type, "NametagsEnabled", Instance).GetValue(computer)!)
            return Failure("Custom names are disabled in your game settings.");
        PropertyInfo state = Property(type, "currentState", Instance);
        string previousState = state.GetValue(computer)?.ToString() ?? "";
        if (previousState == "Loading" || previousState == "Startup" || previousState == "NameWarning")
            return Failure("Wait for the game computer to finish its current name check.");
        FieldInfo current = Field(type, "currentName", Instance);
        FieldInfo saved = Field(type, "savedName", Instance);
        string savedBefore = saved.GetValue(computer)?.ToString() ?? "";
        if (string.Equals(savedBefore, requested, StringComparison.OrdinalIgnoreCase))
            return Success("Your name is already " + requested + ".");
        MethodInfo process = type.GetMethod("ProcessNameState", Instance) ?? throw new MissingMethodException(type.FullName, "ProcessNameState");
        ParameterInfo[] parameters = process.GetParameters();
        if (parameters.Length != 1 || !parameters[0].ParameterType.IsEnum)
            return Failure("This game's name controls are not supported.");
        object enter = Enum.Parse(parameters[0].ParameterType, "enter", false);
        object? original = current.GetValue(computer);
        current.SetValue(computer, requested);
        try
        {
            // Enter preserves the normal KID permission, enabled-nametag and
            // server moderation path. Its callback applies prefs and the RPC.
            process.Invoke(computer, new[] { enter });
        }
        catch
        {
            if (state.GetValue(computer)?.ToString() == previousState && saved.GetValue(computer)?.ToString() == savedBefore)
                current.SetValue(computer, original);
            throw;
        }
        if (string.Equals(saved.GetValue(computer)?.ToString(), requested, StringComparison.OrdinalIgnoreCase))
            return Success("Name set to " + requested + ".");
        if (state.GetValue(computer)?.ToString() == "Loading")
            return Success("Requested name " + requested + ". Waiting for the game's name check.");
        // A permission rejection must not leave the editable name changed.
        if (state.GetValue(computer)?.ToString() == previousState && saved.GetValue(computer)?.ToString() == savedBefore)
            current.SetValue(computer, original);
        return Failure("The game did not allow that name change.");
    }

    private ActionResult SetMic(string argument)
    {
        string requested = (argument ?? "").Trim().ToUpperInvariant();
        string option;
        switch (requested)
        {
            case "OPEN MIC": option = "option1"; break;
            case "PUSH TO TALK": option = "option2"; break;
            case "PUSH TO MUTE": option = "option3"; break;
            default: return Failure("Say open mic, push to mute, or push to talk.");
        }
        Type type = GameType("GorillaNetworking.GorillaComputer");
        object computer = Singleton(type, "instance");
        MethodInfo process = type.GetMethod("ProcessMicState", Instance) ?? throw new MissingMethodException(type.FullName, "ProcessMicState");
        ParameterInfo[] parameters = process.GetParameters();
        if (parameters.Length != 1 || !parameters[0].ParameterType.IsEnum)
            return Failure("This game's microphone controls are not supported.");
        FieldInfo mode = Field(type, "pttType", Instance);
        // This changes only the game's PTT setting and normal saved preference.
        // It does not enable voice-chat permissions or change Jace's capture.
        process.Invoke(computer, new[] { Enum.Parse(parameters[0].ParameterType, option, false) });
        return mode.GetValue(computer)?.ToString() == requested
            ? Success("Game mic set to " + requested.ToLowerInvariant() + ".")
            : Failure("The game could not change its microphone mode.");
    }

    private ActionResult ChangeCosmetic(string argument, bool equip)
    {
        CosmeticApi api = new CosmeticApi(GameType("GorillaNetworking.CosmeticsController"));
        string requested = Normalize(argument ?? "");
        if (requested.Length == 0) return Failure("Say the cosmetic's wardrobe name.");
        List<object> owned = api.OwnedItems();
        if (owned.Count == 0) return Failure("Your owned wardrobe inventory has not loaded yet.");
        List<object> matches = MatchCosmetics(api, owned, requested);
        if (matches.Count == 0) return Failure("No owned cosmetic matches \"" + argument + "\". Say its wardrobe name, or a distinctive part of it.");
        if (matches.Count > 1)
        {
            List<string> names = new List<string>();
            for (int i = 0; i < matches.Count && i < 4; i++) names.Add(api.Label(matches[i]));
            return Failure("That name is ambiguous: " + string.Join(", ", names) + ". Say the full wardrobe name.");
        }
        object selected = matches[0];
        Dictionary<string, object> inventory = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (object item in owned) inventory[api.Id(item)] = item;
        List<object> items = new List<object>();
        api.ExpandOwned(selected, inventory, items, new HashSet<string>(StringComparer.Ordinal));
        Array worn = api.WornItems();
        foreach (object item in items)
        {
            string category = api.Category(item);
            if (category == "Collectable" || category == "None" || category == "Count")
                return Failure(api.Label(item) + " is not a wearable wardrobe item.");
            // Refuse categories the inspected normal wardrobe API cannot place.
            if (!api.CanPlace(item)) return Failure("This game version cannot place " + api.Label(item) + " with its normal wardrobe controls.");
            if (!equip)
            {
                string display = api.Display(item);
                foreach (object existing in worn)
                    if (api.Display(existing) == display && api.Id(existing) != api.Id(item) && !api.IsNull(existing))
                        return Failure("That cosmetic shares a removal name with another worn item; remove it in the wardrobe.");
            }
        }
        bool changed = false;
        foreach (object item in items)
        {
            bool wearing = ContainsItem(api, api.WornItems(), api.Id(item));
            if (equip && !wearing)
            {
                // Ordinary right-hand wardrobe press; isTempCosm is always
                // false. Only items in unlockedCosmetics reach this call.
                api.Press.Invoke(api.Controller, new[] { item, (object)false, false });
                changed = true;
            }
            else if (!equip && wearing)
            {
                api.Remove.Invoke(api.Controller, new[] { api.WornSet, api.Display(item), (object)true });
                changed = true;
            }
        }
        if (changed && !equip)
        {
            api.Update.Invoke(api.Controller, new object[] { true });
            if (api.Updated.GetValue(api.Controller) is Action updated) updated();
        }
        bool achieved = true;
        foreach (object item in items)
            if (ContainsItem(api, api.WornItems(), api.Id(item)) != equip) achieved = false;
        if (!achieved) return Failure("The wardrobe could not complete that change. Check the item's slot in the wardrobe.");
        string label = api.Label(selected);
        return Success(changed ? (equip ? "Equipped " : "Unequipped ") + label + "." :
            label + (equip ? " is already equipped." : " is already unequipped."));
    }

    private static List<object> MatchCosmetics(CosmeticApi api, List<object> owned, string name)
    {
        List<object> matches = new List<object>();
        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (object item in owned)
        {
            string label = Normalize(api.Label(item));
            string catalogName = Normalize(api.Display(item));
            // The actual shop/wardrobe label is overrideDisplayName in the
            // supplied game. Older catalog items can have a human displayName
            // too; accept that spelling without requiring an internal item ID.
            bool match = label == name || catalogName == name;
            if (match && ids.Add(api.Id(item))) matches.Add(item);
        }
        if (matches.Count > 0 || name.Length < 4) return matches;
        // A distinctive partial name is useful when dictation misses a leading
        // adjective. Only a unique owned match can reach the wardrobe action;
        // several matches are listed for the user instead of guessed.
        foreach (object item in owned)
            if (Normalize(api.Label(item)).Contains(name) && ids.Add(api.Id(item))) matches.Add(item);
        return matches;
    }

    // Used to teach the offline recognizer the exact names from the user's
    // loaded, owned wardrobe. Never includes locked/catalog-only cosmetics.
    internal IReadOnlyList<string> GetOwnedCosmeticNames()
    {
        if (!OnMainThread || disposed) return Array.Empty<string>();
        try
        {
            CosmeticApi api = new CosmeticApi(GameType("GorillaNetworking.CosmeticsController"));
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object item in api.OwnedItems())
            {
                string name = api.Label(item).Trim();
                bool clean = name.Length > 0 && name.Length <= 64;
                foreach (char character in name)
                    if (char.IsControl(character) || character == '|' || character == '\u2028' || character == '\u2029') clean = false;
                // The speech helper derives pronunciations and maps them to
                // this canonical name. Exporting aliases as extra items would
                // make equivalent spellings look like ambiguous cosmetics.
                if (clean) names.Add(name);
            }
            var result = new string[names.Count];
            names.CopyTo(result);
            Array.Sort(result, StringComparer.OrdinalIgnoreCase);
            if (result.Length <= 4096) return result;
            var bounded = new string[4096];
            Array.Copy(result, bounded, bounded.Length);
            return bounded;
        }
        catch { return Array.Empty<string>(); } // Inventory can still be loading.
    }

    private ActionResult EquipOutfit(string argument)
    {
        if (!int.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number < 1)
            return Failure("Say an outfit number starting at one, for example equip outfit two.");
        Type type = GameType("GorillaNetworking.CosmeticsController");
        object controller = Singleton(type, "instance");
        int maximum = (int)Field(type, "maxOutfits", Static).GetValue(null)!;
        if (maximum < 1) return Failure("Your saved outfits are still loading. Try again in a moment.");
        if (number > maximum)
            return Failure("Your wardrobe has outfits one through " + maximum.ToString(CultureInfo.InvariantCulture) + ".");
        if (!(bool)Method(type, "CanScrollOutfits", Static).Invoke(null, Array.Empty<object>())!)
            return Failure("Your outfits are loading or saving. Try again in a moment.");
        int index = number - 1;
        PropertyInfo selected = Property(type, "SelectedOutfit", Static);
        if ((int)selected.GetValue(null)! == index)
            return Success("Outfit " + number.ToString(CultureInfo.InvariantCulture) + " is already equipped.");
        Array? saved = Field(type, "savedOutfits", Instance).GetValue(controller) as Array;
        if (saved == null || index >= saved.Length || saved.GetValue(index) == null)
            return Failure("That saved outfit is not ready yet.");
        // The real wardrobe handles ownership, subscriber slot limits, the
        // previous outfit save, color, prefs and normal cosmetic synchronization.
        Method(type, "LoadSavedOutfit", Instance, typeof(int)).Invoke(controller, new object[] { index });
        if ((int)selected.GetValue(null)! != index)
            return Failure("The wardrobe could not switch outfits. Try again after it finishes saving.");
        return Success("Equipped outfit " + number.ToString(CultureInfo.InvariantCulture) + ".");
    }

    private static bool ContainsItem(CosmeticApi api, Array items, string id)
    {
        foreach (object item in items)
            if (!api.IsNull(item) && api.Id(item) == id) return true;
        return false;
    }

    private ActionResult SetTime(string argument)
    {
        string requested = Normalize(argument ?? "");
        if (requested == "normal" || requested == "default" || requested == "auto" || requested == "automatic" || requested == "reset" || requested == "restore")
        {
            if (time != null) RestoreTime();
            else
            {
                Type type = GameType("BetterDayNightManager");
                object manager = Singleton(type, "instance");
                Method(type, "ClearTimeOfDay", Instance, typeof(bool)).Invoke(manager, new object[] { true });
            }
            return Success("Local time restored.");
        }
        Type managerType = GameType("BetterDayNightManager");
        object current = Singleton(managerType, "instance");
        if (Field(managerType, "animatingLightFlash", Instance).GetValue(current) != null)
            return Failure("Wait for the current lighting animation to finish.");
        if (!(bool)Field(managerType, "computerInit", Instance).GetValue(current)!)
        {
            // Offline maps can run the normal UTC bootstrap indefinitely with
            // computerInit false. Let the game's own clock initialize first;
            // setting Static before that bootstrap would be overwritten.
            Method(managerType, "UpdateTimeOfDay", Instance, typeof(bool)).Invoke(current, new object[] { true });
            if (!(bool)Field(managerType, "computerInit", Instance).GetValue(current)! &&
                (double)Field(managerType, "baseSeconds", Instance).GetValue(current)! == 0)
                return Failure("The local day and night clock is still initializing.");
        }
        if (managerType.GetField("timeIndexOverrideFunc", Instance)?.GetValue(current) != null)
            return Failure("The current map controls its own lighting, so its time cannot be changed here.");
        string[] labels = (string[])Field(managerType, "dayNightLightmapNames", Instance).GetValue(current)!;
        double[] ranges = (double[])Property(managerType, "timeOfDayRange", Instance).GetValue(current)!;
        if (labels == null || ranges == null || labels.Length == 0 || labels.Length != ranges.Length)
            return Failure("The current map's time choices are not ready.");
        int index = ResolveTime(requested, labels, ranges);
        if (index < 0) return Failure("Time is not available. Try " + string.Join(", ", labels) + ", or normal.");
        if (time != null && !ReferenceEquals(time.Manager, current)) RestoreTime();
        TimeSnapshot snapshot = time ?? new TimeSnapshot(current, managerType);
        EnsureTimeGuard(managerType);
        snapshot.SelectedIndex = index;
        time = snapshot; // Guard also covers synchronous callbacks from the API.
        try
        {
            snapshot.Set.Invoke(current, new object[] { index, true });
            if (snapshot.Setting.GetValue(current)?.ToString() != "Static" ||
                (int)snapshot.LastSent.GetValue(current)! != index)
            {
                RestoreTime();
                return Failure("The current map could not apply that local time.");
            }
        }
        catch
        {
            RestoreTime();
            throw;
        }
        return Success((int)snapshot.Index.GetValue(current)! == index
            ? "Local time set to " + labels[index] + "."
            : "Requested local time " + labels[index] + ". Waiting for the lighting update.");
    }

    private static int ResolveTime(string requested, string[] labels, double[] ranges)
    {
        for (int i = 0; i < labels.Length; i++) if (Normalize(labels[i] ?? "") == requested) return i;
        if (int.TryParse(requested, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
            return index >= 0 && index < labels.Length ? index : -1;
        double hour;
        switch (requested)
        {
            case "day": case "noon": case "midday": hour = 12; break;
            case "night": case "midnight": hour = 0; break;
            case "dawn": case "sunrise": case "morning": hour = 6; break;
            case "afternoon": hour = 15; break;
            case "sunset": case "evening": hour = 18; break;
            default:
                bool am = requested.EndsWith("am", StringComparison.Ordinal);
                bool pm = requested.EndsWith("pm", StringComparison.Ordinal);
                if ((!am && !pm) || !double.TryParse(requested.Substring(0, requested.Length - 2).Trim(), NumberStyles.None,
                        CultureInfo.InvariantCulture, out hour) || hour < 1 || hour > 12) return -1;
                hour = hour % 12 + (pm ? 12 : 0);
                break;
        }
        int closest = -1;
        double smallestDistance = 25;
        for (int i = 0; i < labels.Length; i++)
        {
            if (!TryPresetHour(Normalize(labels[i] ?? ""), out double presetHour)) continue;
            // Preset labels are actual clock phases; serialized summer/winter
            // durations need not sum to 24 and index zero need not be midnight.
            double distance = (hour - presetHour + 24) % 24;
            if (distance < smallestDistance) { smallestDistance = distance; closest = i; }
        }
        return closest;
    }

    private static bool TryPresetHour(string label, out double hour)
    {
        switch (label)
        {
            case "night": case "midnight": hour = 0; return true;
            case "dawn": case "sunrise": case "morning": hour = 6; return true;
            case "day": case "noon": case "midday": hour = 12; return true;
            case "afternoon": hour = 15; return true;
            case "sunset": case "evening": hour = 18; return true;
        }
        bool am = label.EndsWith("am", StringComparison.Ordinal), pm = label.EndsWith("pm", StringComparison.Ordinal);
        if ((am || pm) && double.TryParse(label.Substring(0, label.Length - 2), NumberStyles.None,
                CultureInfo.InvariantCulture, out hour) && hour >= 1 && hour <= 12)
        { hour = hour % 12 + (pm ? 12 : 0); return true; }
        hour = 0;
        return false;
    }

    private void EnsureTimeGuard(Type managerType)
    {
        if (timeGuardOwner != null && !ReferenceEquals(timeGuardOwner, this))
            throw new InvalidOperationException("Another local-time controller is already active.");
        if (harmony == null)
        {
            MethodInfo target = Method(managerType, "OnPlayerJoined", Instance, GameType("NetPlayer"));
            Harmony candidate = new Harmony(PatchId);
            try
            {
                candidate.Patch(target,
                    prefix: new HarmonyMethod(typeof(GameActions).GetMethod(nameof(BeforePlayerJoined), Static)),
                    finalizer: new HarmonyMethod(typeof(GameActions).GetMethod(nameof(AfterPlayerJoined), Static)));
                harmony = candidate;
            }
            catch
            {
                candidate.UnpatchSelf();
                throw;
            }
        }
        timeGuardOwner = this;
    }

    // SetTimeOfDay is local, but the game normally rebroadcasts any Static time
    // to joining players. Run that callback with the pre-command time state so
    // our visual override never becomes a room setting. Weather is untouched.
    private static void BeforePlayerJoined(object __instance, out JoinState? __state)
    {
        __state = null;
        GameActions? owner = timeGuardOwner;
        TimeSnapshot? snapshot = owner?.time;
        if (owner == null || !owner.OnMainThread || snapshot == null || !ReferenceEquals(snapshot.Manager, __instance)) return;
        JoinState state = new JoinState(snapshot, snapshot.Setting.GetValue(__instance)!,
            (int)snapshot.LastSent.GetValue(__instance)!);
        __state = state; // Finalizer restores even if the second write fails.
        snapshot.Setting.SetValue(__instance, snapshot.OriginalSetting);
        snapshot.LastSent.SetValue(__instance, snapshot.OriginalLastSent);
    }

    private static Exception? AfterPlayerJoined(object __instance, JoinState? __state, Exception? __exception)
    {
        if (__state != null)
        {
            try
            {
                __state.Snapshot.Setting.SetValue(__instance, __state.Setting);
                __state.Snapshot.LastSent.SetValue(__instance, __state.LastSent);
            }
            catch (Exception restoreError)
            {
                return __exception ?? restoreError;
            }
        }
        return __exception;
    }

    private void RestoreTime()
    {
        TimeSnapshot? snapshot = time;
        time = null;
        if (snapshot == null || !Alive(snapshot.Manager)) return;
        if (snapshot.OriginalSetting.ToString() == "Static")
            snapshot.Set.Invoke(snapshot.Manager, new object[] { snapshot.OriginalIndex, true });
        else snapshot.Clear.Invoke(snapshot.Manager, new object[] { true });
    }

    private void ActiveSceneChanged(Scene before, Scene after) => RestoreOnSceneChange();
    private void SceneLoaded(Scene scene, LoadSceneMode mode) => RestoreOnSceneChange();
    private void SceneUnloaded(Scene scene) => RestoreOnSceneChange();
    private void RestoreOnSceneChange()
    {
        if (!OnMainThread || disposed) return;
        try { RestoreTime(); } catch { time = null; }
    }

    public void Dispose()
    {
        if (disposed) return;
        if (!OnMainThread) throw new InvalidOperationException("Dispose GameActions on the Unity main thread.");
        try { RestoreTime(); }
        finally
        {
            disposed = true;
            SceneManager.activeSceneChanged -= ActiveSceneChanged;
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneUnloaded -= SceneUnloaded;
            if (ReferenceEquals(timeGuardOwner, this)) timeGuardOwner = null;
            harmony?.UnpatchSelf();
            harmony = null;
        }
    }

    private bool OnMainThread => Thread.CurrentThread.ManagedThreadId == mainThread;
    private Type GameType(string name)
    {
        if (game == null)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetName().Name == "Assembly-CSharp") { game = assembly; break; }
        }
        return game?.GetType(name, false) ?? throw new MissingMemberException("Game type " + name + " is unavailable.");
    }

    private static object Singleton(Type type, string name)
    {
        object? value = type.GetField(name, Static)?.GetValue(null) ?? type.GetProperty(name, Static)?.GetValue(null);
        return RequireAlive(value, type.Name + " is not ready.");
    }
    private static object RequireAlive(object? value, string message) => Alive(value) ? value! : throw new InvalidOperationException(message);
    private static bool Alive(object? value) => value != null && (!(value is UnityEngine.Object unity) || unity != null);
    private static FieldInfo Field(Type type, string name, BindingFlags flags) => type.GetField(name, flags) ??
        throw new MissingFieldException(type.FullName, name);
    private static PropertyInfo Property(Type type, string name, BindingFlags flags) => type.GetProperty(name, flags) ??
        throw new MissingMemberException(type.FullName, name);
    private static MethodInfo Method(Type type, string name, BindingFlags flags, params Type[] parameters) =>
        type.GetMethod(name, flags, null, parameters, null) ?? throw new MissingMethodException(type.FullName, name);
    private static string Normalize(string value)
    {
        StringBuilder result = new StringBuilder();
        foreach (char c in value) if (char.IsLetterOrDigit(c)) result.Append(char.ToLowerInvariant(c));
        return result.ToString();
    }
    private static string Describe(CommandKind kind) => kind switch
    {
        CommandKind.JoinRoom => "join the room",
        CommandKind.SetColor => "change your color",
        CommandKind.SetTime => "change local time",
        CommandKind.Equip => "equip that cosmetic",
        CommandKind.Unequip => "remove that cosmetic",
        CommandKind.EquipOutfit => "load that outfit",
        CommandKind.SetName => "change your name",
        CommandKind.SetMic => "change the game microphone mode",
        _ => "run that command"
    };
    private static ActionResult Success(string message) => new ActionResult(true, message);
    private static ActionResult Failure(string message) => new ActionResult(false, message);

    private sealed class JoinState
    {
        internal readonly TimeSnapshot Snapshot;
        internal readonly object Setting;
        internal readonly int LastSent;
        internal JoinState(TimeSnapshot snapshot, object setting, int lastSent)
        { Snapshot = snapshot; Setting = setting; LastSent = lastSent; }
    }

    private sealed class TimeSnapshot
    {
        internal readonly object Manager, OriginalSetting;
        internal readonly FieldInfo Setting, Index, LastSent;
        internal readonly int OriginalIndex, OriginalLastSent;
        internal readonly MethodInfo Set, Clear;
        internal int SelectedIndex;
        internal TimeSnapshot(object manager, Type type)
        {
            Manager = manager;
            Setting = Field(type, "currentSetting", Instance);
            Index = Field(type, "currentTimeIndex", Instance);
            LastSent = Field(type, "lastSentTimeIndex", Instance);
            OriginalSetting = Setting.GetValue(manager)!;
            OriginalIndex = (int)Index.GetValue(manager)!;
            OriginalLastSent = (int)LastSent.GetValue(manager)!;
            if (OriginalSetting.ToString() != "Static" && OriginalSetting.ToString() != "Normal")
                throw new InvalidOperationException("The game's current time mode is not supported.");
            string[] labels = (string[])Field(type, "dayNightLightmapNames", Instance).GetValue(manager)!;
            if (OriginalIndex < 0 || OriginalIndex >= labels.Length || OriginalLastSent < -1 || OriginalLastSent >= labels.Length)
                throw new InvalidOperationException("The game's current time selection is not ready.");
            Set = Method(type, "SetTimeOfDay", Instance, typeof(int), typeof(bool));
            Clear = Method(type, "ClearTimeOfDay", Instance, typeof(bool));
        }
    }

    private sealed class CosmeticApi
    {
        internal readonly object Controller, WornSet;
        internal readonly MethodInfo Press, Remove, Update;
        internal readonly FieldInfo Updated;
        private readonly FieldInfo unlocked, wornItems, id, display, label, isNull, category, bundled, holdable;
        private readonly MethodInfo categorySlot;
        private readonly object slotCount;
        internal CosmeticApi(Type type)
        {
            Controller = Singleton(type, "instance");
            Type item = type.GetNestedType("CosmeticItem", BindingFlags.Public) ?? throw new MissingMemberException("CosmeticItem is unavailable.");
            Type set = type.GetNestedType("CosmeticSet", BindingFlags.Public) ?? throw new MissingMemberException("CosmeticSet is unavailable.");
            Type slots = type.GetNestedType("CosmeticSlots", BindingFlags.Public) ?? throw new MissingMemberException("CosmeticSlots is unavailable.");
            Type categories = type.GetNestedType("CosmeticCategory", BindingFlags.Public) ?? throw new MissingMemberException("CosmeticCategory is unavailable.");
            unlocked = Field(type, "unlockedCosmetics", Instance);
            WornSet = RequireAlive(Field(type, "currentWornSet", Instance).GetValue(Controller), "Your wardrobe is not ready.");
            wornItems = Field(set, "items", Instance);
            id = Field(item, "itemName", Instance);
            display = Field(item, "displayName", Instance);
            label = Field(item, "overrideDisplayName", Instance);
            isNull = Field(item, "isNullItem", Instance);
            category = Field(item, "itemCategory", Instance);
            bundled = Field(item, "bundledItems", Instance);
            holdable = Field(item, "isHoldable", Instance);
            Press = Method(type, "PressWardrobeItemButton", Instance, item, typeof(bool), typeof(bool));
            Remove = Method(type, "RemoveCosmeticItemFromSet", Instance, set, typeof(string), typeof(bool));
            Update = Method(type, "UpdateWornCosmetics", Instance, typeof(bool));
            Updated = Field(type, "OnCosmeticsUpdated", Instance);
            categorySlot = Method(type, "CategoryToNonTransferrableSlot", Static, categories);
            slotCount = Enum.Parse(slots, "Count", false);
            WornItems();
        }
        internal string Id(object item) => (string?)id.GetValue(item) ?? "";
        internal string Display(object item) => (string?)display.GetValue(item) ?? "";
        internal string Label(object item)
        {
            string name = (string?)label.GetValue(item) ?? "";
            return string.IsNullOrWhiteSpace(name) ? Display(item) : name;
        }
        internal bool IsNull(object item) => (bool)isNull.GetValue(item)! || string.IsNullOrEmpty(Id(item));
        internal string Category(object item) => category.GetValue(item)?.ToString() ?? "";
        internal bool CanPlace(object item) => (bool)holdable.GetValue(item)! ||
            !Equals(categorySlot.Invoke(null, new[] { category.GetValue(item) }), slotCount);
        internal Array WornItems() => wornItems.GetValue(WornSet) as Array ?? throw new InvalidOperationException("Your worn cosmetics are not ready.");
        internal List<object> OwnedItems()
        {
            IEnumerable items = unlocked.GetValue(Controller) as IEnumerable ?? throw new InvalidOperationException("Your owned inventory is not ready.");
            List<object> result = new List<object>();
            foreach (object item in items) if (!IsNull(item)) result.Add(item);
            return result;
        }
        internal void ExpandOwned(object item, Dictionary<string, object> inventory, List<object> result, HashSet<string> visited)
        {
            string itemId = Id(item);
            if (!inventory.ContainsKey(itemId)) throw new InvalidOperationException("That cosmetic is not in your owned wardrobe.");
            if (!visited.Add(itemId)) throw new InvalidOperationException("This wardrobe set contains duplicate or recursive items.");
            if (Category(item) == "Set")
            {
                string[] children = bundled.GetValue(item) as string[] ?? throw new InvalidOperationException("This wardrobe set is not ready.");
                if (children.Length == 0) throw new InvalidOperationException("This wardrobe set is empty.");
                foreach (string childId in children)
                {
                    if (!inventory.TryGetValue(childId, out object? child))
                        throw new InvalidOperationException("Every part of that set must already be in your owned wardrobe.");
                    ExpandOwned(child, inventory, result, visited);
                }
            }
            else result.Add(item);
        }
    }
}
