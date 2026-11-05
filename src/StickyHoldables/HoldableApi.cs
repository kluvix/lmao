using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace StickyHoldables;

// Reflection keeps game assemblies out of the distributable. Require the known
// contract before installing any hooks; unsupported versions retain vanilla behavior.
internal sealed class HoldableApi
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Type itemType;
    private readonly MethodInfo isLocal, isMine;
    private readonly FieldInfo state, sceneObject, equipmentInstance, cosmeticsInstance;
    private readonly FieldInfo leftItem, rightItem, leftHand, rightHand;
    private readonly object onLeftArm, onRightArm, inLeftHand, inRightHand;
    private readonly Dictionary<object, (object Original, object Attached)> changes = new(ReferenceComparer.Instance);
    private int suspendDepth;

    internal bool Enabled { get; private set; }
    internal MethodInfo Release { get; }
    internal MethodInfo HoldingQuery { get; }
    internal MethodInfo CosmeticChange { get; }
    internal IReadOnlyList<MethodInfo> ReleaseTargets { get; }
    internal IReadOnlyList<MethodInfo> EnableTargets { get; }
    internal IReadOnlyList<MethodInfo> DisableTargets { get; }
    internal Type ItemType => itemType;
    internal bool Suspended => suspendDepth > 0;

    internal HoldableApi(Assembly game)
    {
        itemType = RequireType(game, "TransferrableObject");
        Type equipment = RequireType(game, "EquipmentInteractor");
        Type cosmetics = RequireType(game, "GorillaNetworking.CosmeticsController");
        isLocal = RequireMethod(itemType, "IsLocalObject", m => m.ReturnType == typeof(bool) && m.GetParameters().Length == 0);
        isMine = RequireMethod(itemType, "IsMyItem", m => m.ReturnType == typeof(bool) && m.GetParameters().Length == 0);
        state = RequireField(itemType, "currentState", Instance);
        sceneObject = RequireField(itemType, "isSceneObject", Instance);
        if (!state.FieldType.IsEnum || sceneObject.FieldType != typeof(bool))
            throw new NotSupportedException("Unsupported holdable state/locality fields.");
        onLeftArm = Enum.Parse(state.FieldType, "OnLeftArm");
        onRightArm = Enum.Parse(state.FieldType, "OnRightArm");
        inLeftHand = Enum.Parse(state.FieldType, "InLeftHand");
        inRightHand = Enum.Parse(state.FieldType, "InRightHand");
        equipmentInstance = RequireField(equipment, "instance", Static);
        cosmeticsInstance = RequireField(cosmetics, "instance", Static);
        leftItem = RequireField(equipment, "leftHandHeldEquipment", Instance);
        rightItem = RequireField(equipment, "rightHandHeldEquipment", Instance);
        leftHand = RequireField(equipment, "leftHand", Instance);
        rightHand = RequireField(equipment, "rightHand", Instance);
        Release = RequireMethod(itemType, "OnRelease", m =>
        {
            ParameterInfo[] p = m.GetParameters();
            return m.ReturnType == typeof(bool) && p.Length == 2 &&
                   p[0].ParameterType.Name == "DropZone" && p[1].ParameterType.FullName == "UnityEngine.GameObject";
        });
        HoldingQuery = RequireMethod(equipment, "GetIsHolding", m =>
        {
            ParameterInfo[] p = m.GetParameters();
            return m.ReturnType == typeof(bool) && p.Length == 1 &&
                   p[0].ParameterType.IsEnum && p[0].ParameterType.FullName == "UnityEngine.XR.XRNode";
        });
        CosmeticChange = RequireMethod(cosmetics, "ApplyCosmeticItemToSet", m =>
        {
            ParameterInfo[] p = m.GetParameters();
            return p.Length == 5 && p[0].ParameterType.Name == "CosmeticSet" &&
                   p[1].ParameterType.Name == "CosmeticItem" && p[2].ParameterType == typeof(bool) &&
                   p[3].ParameterType == typeof(bool) && p[4].ParameterType.IsGenericType &&
                   p[4].ParameterType.GetGenericTypeDefinition() == typeof(List<>) &&
                   p[4].ParameterType.GetGenericArguments()[0].Name == "CosmeticSlots";
        });
        ReleaseTargets = FindTargets(game, Release, overridesOnly: true);
        EnableTargets = FindTargets(game, RequireMethod(itemType, "OnEnable", NoArguments), overridesOnly: false);
        DisableTargets = FindTargets(game, RequireMethod(itemType, "OnDisable", NoArguments), overridesOnly: false);
    }

    internal bool IsEligible(object? item)
    {
        if (item == null || !itemType.IsInstanceOfType(item)) return false;
        for (Type? t = item.GetType(); t != null; t = t.BaseType)
        {
            if (t.Name is "Slingshot" or "ThrowableBug" or "ThrowableSetDressing" or "DecorativeItem") return false;
        }
        // IsMyItem alone returns true during some uninitialized game states.
        return !(bool)sceneObject.GetValue(item)! && (bool)isLocal.Invoke(item, null)! && (bool)isMine.Invoke(item, null)!;
    }

    internal bool Attach(object item)
    {
        if (!Enabled || Suspended || !IsEligible(item)) return false;
        object? current = state.GetValue(item);
        object? attached = Equals(current, onLeftArm) ? inRightHand : Equals(current, onRightArm) ? inLeftHand : null;
        if (attached == null) return false;
        // Old forearm cosmetics are grabbed by the opposite hand. Use the game's
        // own hand poses rather than guessing world-space parenting/offsets.
        changes[item] = (current!, attached);
        state.SetValue(item, attached);
        return true;
    }

    internal void Forget(object item) => changes.Remove(item);

    internal bool ShouldKeep(object item, object? dropZone, object? releasingHand)
    {
        if (!Enabled || Suspended || dropZone != null || !IsEligible(item)) return false;
        object? equipment = equipmentInstance.GetValue(null);
        if (equipment == null || releasingHand == null) return false;
        object? current = state.GetValue(item);
        return (Equals(current, inLeftHand) && ReferenceEquals(leftItem.GetValue(equipment), item) &&
                ReferenceEquals(leftHand.GetValue(equipment), releasingHand)) ||
               (Equals(current, inRightHand) && ReferenceEquals(rightItem.GetValue(equipment), item) &&
                ReferenceEquals(rightHand.GetValue(equipment), releasingHand));
    }

    internal bool IgnoreHoldingQuery(object equipment, object? node)
    {
        if (!Enabled || Suspended || !ReferenceEquals(equipment, equipmentInstance.GetValue(null))) return false;
        bool left = node?.ToString() == "LeftHand";
        if (!left && node?.ToString() != "RightHand") return false;
        object? item = (left ? leftItem : rightItem).GetValue(equipment);
        return IsEligible(item) && Equals(state.GetValue(item), left ? inLeftHand : inRightHand);
    }

    internal bool BeginCosmeticChange(object controller)
    {
        if (!Enabled || !ReferenceEquals(controller, cosmeticsInstance.GetValue(null))) return false;
        suspendDepth++;
        try { ReleaseManagedHands(); }
        catch { suspendDepth--; throw; }
        return true;
    }

    internal void EndCosmeticChange(bool entered)
    {
        if (entered && suspendDepth > 0) suspendDepth--;
    }

    internal void SetEnabled(bool enabled)
    {
        if (Enabled == enabled) return;
        Enabled = enabled;
        if (enabled) return;
        suspendDepth++;
        Exception? failure = null;
        try
        {
            try { ReleaseManagedHands(); }
            catch (Exception e) { failure = e; }
            foreach (var change in changes)
            {
                try
                {
                    if (IsEligible(change.Key) && Equals(state.GetValue(change.Key), change.Value.Attached))
                        state.SetValue(change.Key, change.Value.Original);
                }
                catch (Exception e) { failure ??= e; }
            }
            changes.Clear();
        }
        finally { suspendDepth--; }
        if (failure != null) throw new InvalidOperationException("Could not restore every holdable; re-equip cosmetics to reset them.", failure);
    }

    private void ReleaseManagedHands()
    {
        object? equipment = equipmentInstance.GetValue(null);
        if (equipment == null) return;
        Exception? failure = null;
        foreach (var hand in new[] { (leftItem, leftHand), (rightItem, rightHand) })
        {
            try
            {
                object? item = hand.Item1.GetValue(equipment);
                if (IsEligible(item)) Release.Invoke(item, new[] { null, hand.Item2.GetValue(equipment) });
            }
            catch (Exception e) { failure ??= e; }
        }
        if (failure != null) throw new InvalidOperationException("A held item rejected normal release.", failure);
    }

    private IReadOnlyList<MethodInfo> FindTargets(Assembly game, MethodInfo root, bool overridesOnly)
    {
        var methods = new HashSet<MethodInfo> { root };
        foreach (Type type in game.GetTypes().Where(itemType.IsAssignableFrom))
        foreach (MethodInfo method in type.GetMethods(Instance | BindingFlags.DeclaredOnly))
        {
            if (method.IsAbstract || method.Name != root.Name || method.ReturnType != root.ReturnType ||
                !method.GetParameters().Select(p => p.ParameterType).SequenceEqual(root.GetParameters().Select(p => p.ParameterType))) continue;
            if (!overridesOnly || method.GetBaseDefinition() == root.GetBaseDefinition()) methods.Add(method);
        }
        return methods.ToArray();
    }

    private static bool NoArguments(MethodInfo m) => m.ReturnType == typeof(void) && m.GetParameters().Length == 0;
    private static Type RequireType(Assembly assembly, string name) => assembly.GetType(name, throwOnError: false) ??
        throw new NotSupportedException("Missing game type: " + name);
    private static FieldInfo RequireField(Type type, string name, BindingFlags flags) => type.GetField(name, flags) ??
        throw new NotSupportedException("Missing game field: " + type.Name + "." + name);
    private static MethodInfo RequireMethod(Type type, string name, Func<MethodInfo, bool> predicate)
    {
        MethodInfo[] matches = type.GetMethods(Instance).Where(m => m.Name == name && predicate(m)).ToArray();
        return matches.Length == 1 ? matches[0] : throw new NotSupportedException("Unsupported game method: " + type.Name + "." + name);
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceComparer Instance = new();
        public new bool Equals(object? a, object? b) => ReferenceEquals(a, b);
        public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
    }
}
