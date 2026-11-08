using HeyJace;
using HeyJace.Core;
using GorillaNetworking;

// These fixtures model the statically inspected game boundary. They run only
// our dispatch logic; no supplied game binary or Unity native code is loaded.
internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new InvalidOperationException(message); }
    private static ActionResult Run(GameActions actions, CommandKind kind, string argument) => actions.Execute(new JaceCommand(kind, argument));
    private static CosmeticsController.CosmeticItem Item(string id, string label, CosmeticsController.CosmeticCategory category = CosmeticsController.CosmeticCategory.Hat) =>
        new() { itemName = id, displayName = id, overrideDisplayName = label, itemCategory = category, bundledItems = Array.Empty<string>() };

    private static void Main()
    {
        using var actions = new GameActions();
        ActionResult result = Run(actions, CommandKind.JoinRoom, "banana");
        Check(result.Success && PhotonNetworkController.Instance.LastCode == "BANANA", "Normal room join dispatches the normalized code.");
        Check(PhotonNetworkController.Instance.LastJoinType == JoinType.Solo, "Normal solo join mode is retained.");
        int joins = PhotonNetworkController.Instance.Joins;
        Check(!Run(actions, CommandKind.JoinRoom, "code with spaces").Success && PhotonNetworkController.Instance.Joins == joins, "Invalid room never calls the game.");
        Check(!Task.Run(() => Run(actions, CommandKind.JoinRoom, "OTHER")).GetAwaiter().GetResult().Success, "Worker-thread commands are rejected.");

        var wardrobe = CosmeticsController.instance;
        var hat = Item("LBAAB", "Fancy Top Hat");
        var face = Item("LBAAC", "Summer Shades", CosmeticsController.CosmeticCategory.Face);
        wardrobe.unlockedCosmetics.Add(hat); wardrobe.unlockedCosmetics.Add(face);
        result = Run(actions, CommandKind.Equip, "fancy top hat");
        Check(result.Success && wardrobe.Presses == 1 && !wardrobe.LastTemporary, "Actual cosmetic name takes the ordinary owned wardrobe path.");
        Check(wardrobe.currentWornSet.items[0].itemName == hat.itemName, "Selected named item becomes worn.");
        Check(Run(actions, CommandKind.Equip, "Fancy Top Hat").Success && wardrobe.Presses == 1, "Repeated equip is idempotent, never toggles the item off.");
        Check(!Run(actions, CommandKind.Equip, "Top Hat").Success && wardrobe.Presses == 1, "Partial name does not silently equip a different item.");
        Check(!Run(actions, CommandKind.Equip, "Unowned Fancy Crown").Success, "Locked/catalog-only item cannot be equipped.");
        Check(Run(actions, CommandKind.Unequip, "FANCY TOP HAT").Success && wardrobe.Removes == 1 && wardrobe.Syncs == 1, "Unequip removes and synchronizes normally.");
        Check(Run(actions, CommandKind.Unequip, "Fancy Top Hat").Success && wardrobe.Removes == 1, "Repeated unequip is idempotent.");
        var apostrophe = Item("LBAAD", "Witch's Hat");
        wardrobe.unlockedCosmetics.Add(apostrophe);
        Check(Run(actions, CommandKind.Equip, "witchs hat").Success, "Spoken spelling tolerates an apostrophe.");
        wardrobe.unlockedCosmetics.Add(Item("LBAAE", "Witchs Hat"));
        int presses = wardrobe.Presses;
        Check(!Run(actions, CommandKind.Equip, "witchs hat").Success && wardrobe.Presses == presses, "Duplicate normalized names fail without choosing an arbitrary item.");
        var set = Item("LBAAF", "Summer Set", CosmeticsController.CosmeticCategory.Set);
        set.bundledItems = new[] { hat.itemName, face.itemName };
        wardrobe.unlockedCosmetics.Add(set);
        Check(Run(actions, CommandKind.Equip, "summer set").Success, "Owned set equips owned child cosmetics.");
        set.bundledItems = new[] { "UNOWNED" };
        wardrobe.unlockedCosmetics[wardrobe.unlockedCosmetics.Count - 1] = set;
        presses = wardrobe.Presses;
        Check(!Run(actions, CommandKind.Equip, "summer set").Success && wardrobe.Presses == presses, "A set with an unowned child is refused before mutation.");
        var names = actions.GetOwnedCosmeticNames();
        Check(names.Contains("Fancy Top Hat") && !names.Contains("LBAAB"), "Recognition vocabulary contains actual names rather than item IDs.");
        wardrobe.unlockedCosmetics.Add(Item("BAD", "Bad|Injected"));
        wardrobe.unlockedCosmetics.Add(Item("BAD2", "Bad\nName"));
        wardrobe.unlockedCosmetics.Add(Item("BAD3", new string('x', 65)));
        wardrobe.unlockedCosmetics.Add(Item("BAD4", "Bad\u2028Name"));
        Check(!actions.GetOwnedCosmeticNames().Any(x => x.Contains('|') || x.Contains('\n') || x.Contains('\u2028') || x.Length > 64), "Recognition vocabulary filters oversized/control/delimiter names.");
        for (int i = 0; i < 600; i++) wardrobe.unlockedCosmetics.Add(Item("EXTRA" + i, "Extra Hat " + i));
        Check(actions.GetOwnedCosmeticNames().Count == 512, "Recognition vocabulary stays bounded.");

        Check(Run(actions, CommandKind.EquipOutfit, "2").Success && wardrobe.LoadedIndex == 1 && CosmeticsController.SelectedOutfit == 1, "Voice outfit two selects the normal zero-based saved outfit one.");
        int loads = wardrobe.Loads;
        Check(Run(actions, CommandKind.EquipOutfit, "2").Success && wardrobe.Loads == loads, "Repeated outfit command leaves the selected preset intact.");
        Check(Run(actions, CommandKind.EquipOutfit, "1").Success && wardrobe.LoadedIndex == 0, "First numbered outfit maps to zero.");
        Check(!Run(actions, CommandKind.EquipOutfit, "0").Success, "User outfit zero is invalid.");
        Check(!Run(actions, CommandKind.EquipOutfit, "4").Success, "Outfit beyond the real wardrobe slot limit is refused.");
        CosmeticsController.Ready = false;
        loads = wardrobe.Loads;
        Check(!Run(actions, CommandKind.EquipOutfit, "2").Success && wardrobe.Loads == loads, "Saving/loading wardrobe is not forced to switch.");
        CosmeticsController.Ready = true;
        wardrobe.IgnoreLoad = true;
        Check(!Run(actions, CommandKind.EquipOutfit, "2").Success, "No success claimed when the game's selection does not change.");
        wardrobe.IgnoreLoad = false;

        Check(Run(actions, CommandKind.SetColor, "905").Success, "Numeric Gorilla Tag RGB code reaches the normal color API.");
        Check(Math.Abs(wardrobe.Color.x - 1) < .0001 && wardrobe.Color.y == 0 && Math.Abs(wardrobe.Color.z - 5f / 9) < .0001 && wardrobe.SaveColor, "Digits scale to 0..1 and use normal saved-color path.");
        Check(!Run(actions, CommandKind.SetColor, "red").Success, "Named colors no longer pass the game boundary.");
        NetworkSystem.Instance = null!;
        Check(!Run(actions, CommandKind.SetColor, "905").Success, "Unavailable network/player state gives failure rather than false success.");
        NetworkSystem.Instance = new NetworkSystem();

        Check(Run(actions, CommandKind.SetTime, "night").Success && BetterDayNightManager.instance.currentTimeIndex == 0, "Time command resolves the map's normal preset and calls local API.");
        Check(Run(actions, CommandKind.SetTime, "normal").Success && BetterDayNightManager.instance.currentSetting == BetterDayNightManager.Mode.Normal, "Normal time restores the prior clock mode.");
        Check(!Run(actions, CommandKind.SetTime, "75").Success, "Out-of-range map time is rejected.");
        Check(Run(actions, CommandKind.SetTime, "morning").Success, "Morning alias resolves using map duration data.");
        Run(actions, CommandKind.JoinRoom, "NEXT");
        Check(BetterDayNightManager.instance.currentSetting == BetterDayNightManager.Mode.Normal, "Room join clears the prior local time override.");
        actions.Dispose();
        Check(!Run(actions, CommandKind.JoinRoom, "NO").Success, "Disposed actions cannot mutate the game.");
        Console.WriteLine($"Hey Jace game boundary checks: {checks} assertions passed (fake APIs; no game execution).");
    }
}

namespace UnityEngine
{
    public class Object { }
    public readonly struct Vector3
    {
        public readonly float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { }
    public enum LoadSceneMode { Single }
    public static class SceneManager
    {
        public static event Action<Scene, Scene>? activeSceneChanged;
        public static event Action<Scene, LoadSceneMode>? sceneLoaded;
        public static event Action<Scene>? sceneUnloaded;
        public static void ExerciseEvents() { activeSceneChanged?.Invoke(default, default); sceneLoaded?.Invoke(default, LoadSceneMode.Single); sceneUnloaded?.Invoke(default); }
    }
}
namespace HarmonyLib
{
    public sealed class Harmony
    {
        public Harmony(string id) { }
        public void Patch(System.Reflection.MethodInfo target, HarmonyMethod prefix, HarmonyMethod finalizer) { }
        public void UnpatchSelf() { }
    }
    public sealed class HarmonyMethod { public HarmonyMethod(System.Reflection.MethodInfo? method) { } }
}

public sealed class GorillaTagger
{
    public static GorillaTagger Instance { get; } = new();
    public object offlineVRRig = new();
    public object myVRRig { get; } = new();
}
public sealed class NetworkSystem
{
    public static NetworkSystem Instance = new();
    public bool InRoom => true;
}
public sealed class NetPlayer { }
public sealed class BetterDayNightManager
{
    public static BetterDayNightManager instance = new();
    public bool computerInit = true;
    public object? animatingLightFlash = null;
    public string[] dayNightLightmapNames = { "night", "morning", "day", "evening" };
    public double[] timeOfDayRange => new[] { 6d, 6d, 6d, 6d };
    public enum Mode { Normal, Static }
    public Mode currentSetting = Mode.Normal;
    public int currentTimeIndex = 2, lastSentTimeIndex = 2;
    public void SetTimeOfDay(int index, bool local) { if (!local) throw new Exception("Must remain local."); currentTimeIndex = index; currentSetting = Mode.Static; }
    public void ClearTimeOfDay(bool local) { if (!local) throw new Exception("Must remain local."); currentSetting = Mode.Normal; }
    public void OnPlayerJoined(NetPlayer player) { }
}
namespace GorillaNetworking
{
    public enum JoinType { Solo }
    public sealed class PhotonNetworkController
    {
        public static PhotonNetworkController Instance = new();
        public string LastCode = "";
        public JoinType LastJoinType;
        public int Joins;
        public void AttemptToJoinSpecificRoom(string code, JoinType type) { LastCode = code; LastJoinType = type; Joins++; }
    }
    public sealed class GorillaComputer { public static GorillaComputer instance = new(); }
    public sealed class CosmeticsController
    {
        public enum CosmeticCategory { None, Hat, Face, Set, Collectable, Count }
        public enum CosmeticSlots { Hat, Face, Holdable, Count }
        public struct CosmeticItem
        {
            public string itemName, displayName, overrideDisplayName;
            public bool isNullItem, isHoldable;
            public CosmeticCategory itemCategory;
            public string[] bundledItems;
        }
        public sealed class CosmeticSet
        {
            public CosmeticItem[] items = { Empty, Empty, Empty };
            private static CosmeticItem Empty => new() { itemName = "", displayName = "NOTHING", overrideDisplayName = "NOTHING", isNullItem = true, bundledItems = Array.Empty<string>() };
        }
        public static CosmeticsController instance = new();
        public List<CosmeticItem> unlockedCosmetics = new();
        public CosmeticSet currentWornSet = new();
        public Action? OnCosmeticsUpdated;
        private readonly CosmeticSet[] savedOutfits = { new(), new(), new() };
        private static readonly int maxOutfits = 3;
        public static int SelectedOutfit { get; private set; }
        public static bool Ready = true;
        public bool IgnoreLoad, LastTemporary, SaveColor;
        public int LoadedIndex = -1, Loads, Presses, Removes, Syncs;
        public UnityEngine.Vector3 Color;
        public static bool CanScrollOutfits() => Ready;
        public static CosmeticSlots CategoryToNonTransferrableSlot(CosmeticCategory c) => c switch { CosmeticCategory.Hat => CosmeticSlots.Hat, CosmeticCategory.Face => CosmeticSlots.Face, _ => CosmeticSlots.Count };
        public void PressWardrobeItemButton(CosmeticItem item, bool left, bool temporary) { LastTemporary = temporary; Presses++; currentWornSet.items[(int)CategoryToNonTransferrableSlot(item.itemCategory)] = item; }
        public void RemoveCosmeticItemFromSet(CosmeticSet set, string display, bool prefs) { Removes++; for (int i = 0; i < set.items.Length; i++) if (set.items[i].displayName == display) set.items[i] = new CosmeticItem { isNullItem = true, itemName = "", displayName = "NOTHING", bundledItems = Array.Empty<string>() }; }
        public void UpdateWornCosmetics(bool sync) { if (sync) Syncs++; }
        private void UpdateMonkeColor(UnityEngine.Vector3 color, bool save) { Color = color; SaveColor = save; }
        public void LoadSavedOutfit(int index) { if (!CanScrollOutfits() || IgnoreLoad || index < 0 || index >= maxOutfits) return; SelectedOutfit = index; LoadedIndex = index; Loads++; currentWornSet = savedOutfits[index]; }
    }
}
