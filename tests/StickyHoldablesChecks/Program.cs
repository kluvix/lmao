using System.Reflection;
using System.Reflection.Emit;
using GorillaNetworking;
using StickyHoldables;
using UnityEngine;
using UnityEngine.XR;
using State = TransferrableObject.PositionState;

internal static class Program
{
    private static int passed, failed, assertions;

    private static int Main()
    {
        Run("named arm states attach to the opposite hand and preserve the first snapshot", () =>
        {
            HoldableApi api = Api();
            var left = new TransferrableObject { currentState = State.OnLeftArm };
            var right = new TransferrableObject { currentState = State.OnRightArm };
            True(api.Attach(left)); Equal(State.InRightHand, left.currentState);
            True(api.Attach(right)); Equal(State.InLeftHand, right.currentState);
            False(api.Attach(left));
            api.SetEnabled(false);
            Equal(State.OnLeftArm, left.currentState); Equal(State.OnRightArm, right.currentState);
        });

        Run("remote, unowned, scene, null and non-item objects remain untouched", () =>
        {
            HoldableApi api = Api();
            object?[] excluded =
            {
                null, new object(),
                new TransferrableObject { Local = false, Mine = true },
                new TransferrableObject { Local = true, Mine = false },
                new TransferrableObject { isSceneObject = true }
            };
            foreach (object? item in excluded)
            {
                False(api.IsEligible(item));
                if (item != null) False(api.Attach(item));
            }
        });

        Run("gameplay holdable exclusions include subclasses", () =>
        {
            HoldableApi api = Api();
            TransferrableObject[] excluded =
            {
                new Slingshot(), new ThrowableBug(), new ThrowableSetDressing(), new DecorativeItem(),
                new DerivedSlingshot(), new DerivedThrowableBug(),
                new DerivedThrowableSetDressing(), new DerivedDecorativeItem()
            };
            foreach (TransferrableObject item in excluded)
            {
                False(api.IsEligible(item)); False(api.Attach(item));
                Equal(State.OnLeftArm, item.currentState);
            }
        });

        Run("already held, chest and unassigned states are not auto-attached", () =>
        {
            HoldableApi api = Api();
            foreach (State state in new[] { State.InLeftHand, State.InRightHand, State.OnChest, State.None })
            {
                var item = new TransferrableObject { currentState = state };
                False(api.Attach(item)); Equal(state, item.currentState);
            }
        });

        Run("null-zone release is retained only for the registered matching left hand", () =>
        {
            HoldableApi api = Api(); EquipmentInteractor equipment = EquipmentInteractor.instance!;
            var item = new EqualItem { currentState = State.InLeftHand };
            equipment.leftHandHeldEquipment = item;
            True(api.ShouldKeep(item, null, equipment.leftHand));
            False(api.ShouldKeep(item, null, equipment.rightHand));
            False(api.ShouldKeep(item, null, new GameObject()));
            False(api.ShouldKeep(item, null, null));
            False(api.ShouldKeep(item, new DropZone(), equipment.leftHand));
            False(api.ShouldKeep(new EqualItem { currentState = State.InLeftHand }, null, equipment.leftHand));
        });

        Run("right-hand retention is symmetric and refuses mismatched state or registration", () =>
        {
            HoldableApi api = Api(); EquipmentInteractor equipment = EquipmentInteractor.instance!;
            var item = new TransferrableObject { currentState = State.InRightHand };
            equipment.rightHandHeldEquipment = item;
            True(api.ShouldKeep(item, null, equipment.rightHand));
            False(api.ShouldKeep(item, null, equipment.leftHand));
            item.currentState = State.InLeftHand;
            False(api.ShouldKeep(item, null, equipment.rightHand));
            item.currentState = State.InRightHand;
            equipment.rightHandHeldEquipment = null;
            False(api.ShouldKeep(item, null, equipment.rightHand));
        });

        Run("missing equipment singleton and lost ownership retain vanilla release", () =>
        {
            HoldableApi api = Api(); EquipmentInteractor equipment = EquipmentInteractor.instance!;
            var item = new TransferrableObject { currentState = State.InLeftHand };
            equipment.leftHandHeldEquipment = item;
            item.Local = false; False(api.ShouldKeep(item, null, equipment.leftHand));
            item.Local = true; item.Mine = false; False(api.ShouldKeep(item, null, equipment.leftHand));
            item.Mine = true; EquipmentInteractor.instance = null;
            False(api.ShouldKeep(item, null, equipment.leftHand));
        });

        Run("holding query is scoped to singleton, hand, eligibility and matching state", () =>
        {
            HoldableApi api = Api(); EquipmentInteractor equipment = EquipmentInteractor.instance!;
            var left = new TransferrableObject { currentState = State.InLeftHand };
            var right = new TransferrableObject { currentState = State.InRightHand };
            equipment.leftHandHeldEquipment = left; equipment.rightHandHeldEquipment = right;
            True(api.IgnoreHoldingQuery(equipment, XRNode.LeftHand));
            True(api.IgnoreHoldingQuery(equipment, XRNode.RightHand));
            False(api.IgnoreHoldingQuery(equipment, XRNode.Head));
            False(api.IgnoreHoldingQuery(equipment, null));
            False(api.IgnoreHoldingQuery(new EquipmentInteractor(), XRNode.LeftHand));
            left.currentState = State.OnChest;
            False(api.IgnoreHoldingQuery(equipment, XRNode.LeftHand));
            right.Local = false;
            False(api.IgnoreHoldingQuery(equipment, XRNode.RightHand));
            equipment.leftHandHeldEquipment = new Slingshot { currentState = State.InLeftHand };
            False(api.IgnoreHoldingQuery(equipment, XRNode.LeftHand));
        });

        Run("auto-attached cosmetics mute hover from either local hand without registered equipment", () =>
        {
            HoldableApi api = Api(); EquipmentInteractor equipment = EquipmentInteractor.instance!;
            var leftArm = new TransferrableObject { currentState = State.OnLeftArm };
            var rightArm = new TransferrableObject { currentState = State.OnRightArm };
            True(api.Attach(leftArm)); True(api.Attach(rightArm));
            Equal<object?>(null, equipment.leftHandHeldEquipment);
            Equal<object?>(null, equipment.rightHandHeldEquipment);
            foreach (TransferrableObject item in new[] { leftArm, rightArm })
            {
                True(api.ShouldMuteHover(item, equipment.leftHand));
                True(api.ShouldMuteHover(item, equipment.rightHand));
            }
            Equal(State.InRightHand, leftArm.currentState);
            Equal(State.InLeftHand, rightArm.currentState);
        });

        Run("hover suppression requires an actual local hand and an exact in-hand state", () =>
        {
            HoldableApi api = Api(); EquipmentInteractor equipment = EquipmentInteractor.instance!;
            var item = new TransferrableObject { currentState = State.InLeftHand };
            False(api.ShouldMuteHover(item, null));
            False(api.ShouldMuteHover(item, new GameObject()));
            False(api.ShouldMuteHover(null!, equipment.leftHand));
            foreach (State state in new[] { State.OnLeftArm, State.OnRightArm, State.OnChest, State.None,
                         (State)((int)State.InLeftHand | (int)State.InRightHand) })
            {
                item.currentState = state;
                False(api.ShouldMuteHover(item, equipment.leftHand));
                False(api.ShouldMuteHover(item, equipment.rightHand));
            }
            item.currentState = State.InRightHand;
            EquipmentInteractor.instance = null;
            False(api.ShouldMuteHover(item, equipment.leftHand));
        });

        Run("hover feedback stays vanilla for remote, excluded, disabled and cosmetic-change cases", () =>
        {
            HoldableApi api = Api(); EquipmentInteractor equipment = EquipmentInteractor.instance!;
            TransferrableObject[] excluded =
            {
                new() { Local = false, Mine = true },
                new() { Local = true, Mine = false },
                new() { isSceneObject = true },
                new Slingshot(), new ThrowableBug(), new ThrowableSetDressing(), new DecorativeItem(),
                new DerivedSlingshot(), new DerivedThrowableBug(),
                new DerivedThrowableSetDressing(), new DerivedDecorativeItem()
            };
            foreach (TransferrableObject item in excluded)
            {
                item.currentState = State.InRightHand;
                False(api.ShouldMuteHover(item, equipment.leftHand));
                False(api.ShouldMuteHover(item, equipment.rightHand));
            }
            var owned = new TransferrableObject { currentState = State.InLeftHand };
            True(api.ShouldMuteHover(owned, equipment.leftHand));
            bool entered = api.BeginCosmeticChange(CosmeticsController.instance!);
            True(entered); False(api.ShouldMuteHover(owned, equipment.leftHand));
            api.EndCosmeticChange(entered);
            True(api.ShouldMuteHover(owned, equipment.leftHand));
            api.SetEnabled(false);
            False(api.ShouldMuteHover(owned, equipment.leftHand));
            api.SetEnabled(true);
            True(api.ShouldMuteHover(owned, equipment.leftHand));
        });

        Run("hover method discovery includes genuine overrides without unrelated or hidden methods", () =>
        {
            HoldableApi api = Api();
            True(api.HoverTargets.Any(m => m.DeclaringType == typeof(TransferrableObject)));
            True(api.HoverTargets.Any(m => m.DeclaringType == typeof(OverrideHoldable)));
            False(api.HoverTargets.Any(m => m.DeclaringType == typeof(HiddenLifecycleHoldable)));
            False(api.HoverTargets.Any(m => m.DeclaringType == typeof(AbstractHoldable)));
            False(api.HoverTargets.Any(m => m.DeclaringType == typeof(UnrelatedType)));
            Equal(api.HoverTargets.Count, api.HoverTargets.Distinct().Count());
        });

        Run("disable restores only attached snapshots and never overwrites a new game state", () =>
        {
            HoldableApi api = Api();
            var unchanged = new TransferrableObject();
            var moved = new TransferrableObject();
            var remoteNow = new TransferrableObject();
            True(api.Attach(unchanged)); True(api.Attach(moved)); True(api.Attach(remoteNow));
            moved.currentState = State.OnChest; remoteNow.Local = false;
            api.SetEnabled(false);
            Equal(State.OnLeftArm, unchanged.currentState);
            Equal(State.OnChest, moved.currentState);
            Equal(State.InRightHand, remoteNow.currentState);
            False(api.Enabled); False(api.Suspended); False(api.Attach(new TransferrableObject()));
        });

        Run("snapshot bookkeeping uses object identity even when items compare equal", () =>
        {
            HoldableApi api = Api();
            var first = new EqualItem { currentState = State.OnLeftArm };
            var second = new EqualItem { currentState = State.OnRightArm };
            True(first.Equals(second)); True(api.Attach(first)); True(api.Attach(second));
            api.SetEnabled(false);
            Equal(State.OnLeftArm, first.currentState); Equal(State.OnRightArm, second.currentState);
        });

        Run("forget discards disabled-item snapshots instead of restoring stale state", () =>
        {
            HoldableApi api = Api(); var item = new TransferrableObject();
            True(api.Attach(item)); api.Forget(item); api.SetEnabled(false);
            Equal(State.InRightHand, item.currentState);
        });

        Run("re-enable takes a fresh snapshot after the game changes equipment", () =>
        {
            HoldableApi api = Api(); var item = new TransferrableObject();
            True(api.Attach(item)); api.SetEnabled(false);
            item.currentState = State.OnRightArm;
            api.SetEnabled(true); True(api.Attach(item));
            Equal(State.InLeftHand, item.currentState);
            api.SetEnabled(false); Equal(State.OnRightArm, item.currentState);
        });

        Run("disable releases managed hands through the game's virtual method", () =>
        {
            HoldableApi api = Api(); EquipmentInteractor equipment = EquipmentInteractor.instance!;
            var item = new OverrideHoldable { currentState = State.InLeftHand };
            equipment.leftHandHeldEquipment = item;
            item.ReleaseObserver = (zone, hand) =>
            {
                True(api.Suspended); False(api.Enabled); Equal<DropZone?>(null, zone); Same(equipment.leftHand, hand);
            };
            api.SetEnabled(false);
            Equal(1, item.ReleaseCalls); Equal<object?>(null, equipment.leftHandHeldEquipment);
            False(api.Suspended);
        });

        Run("cosmetic changes suspend retention only for the local controller", () =>
        {
            HoldableApi api = Api(); EquipmentInteractor equipment = EquipmentInteractor.instance!;
            var item = new TransferrableObject { currentState = State.InLeftHand, ChangeStateOnRelease = false };
            equipment.leftHandHeldEquipment = item;
            False(api.BeginCosmeticChange(new CosmeticsController())); False(api.Suspended);
            bool entered = api.BeginCosmeticChange(CosmeticsController.instance!);
            True(entered); True(api.Suspended); Equal(1, item.ReleaseCalls);
            False(api.ShouldKeep(item, null, equipment.leftHand));
            False(api.IgnoreHoldingQuery(equipment, XRNode.LeftHand));
            False(api.Attach(new TransferrableObject()));
            api.EndCosmeticChange(entered);
            False(api.Suspended); True(api.ShouldKeep(item, null, equipment.leftHand));
        });

        Run("nested cosmetic scopes preserve suspension until the outer finalizer exits", () =>
        {
            HoldableApi api = Api(); CosmeticsController controller = CosmeticsController.instance!;
            bool outer = api.BeginCosmeticChange(controller), inner = api.BeginCosmeticChange(controller);
            True(outer); True(inner); True(api.Suspended);
            api.EndCosmeticChange(false); True(api.Suspended);
            api.EndCosmeticChange(inner); True(api.Suspended);
            api.EndCosmeticChange(outer); False(api.Suspended);
            api.EndCosmeticChange(true); False(api.Suspended);
        });

        Run("a release failure still attempts the second hand and unwinds only the failing scope", () =>
        {
            HoldableApi api = Api(); EquipmentInteractor equipment = EquipmentInteractor.instance!;
            bool outer = api.BeginCosmeticChange(CosmeticsController.instance!);
            var left = new TransferrableObject { currentState = State.InLeftHand, ThrowOnRelease = true };
            var right = new TransferrableObject { currentState = State.InRightHand };
            equipment.leftHandHeldEquipment = left; equipment.rightHandHeldEquipment = right;
            Throws<Exception>(() => api.BeginCosmeticChange(CosmeticsController.instance!));
            Equal(1, left.ReleaseCalls); Equal(1, right.ReleaseCalls); True(api.Suspended);
            api.EndCosmeticChange(outer); False(api.Suspended);
        });

        Run("disable attempts both hands, restores snapshots and reports release errors", () =>
        {
            HoldableApi api = Api(); EquipmentInteractor equipment = EquipmentInteractor.instance!;
            var left = new TransferrableObject { currentState = State.OnRightArm, ThrowOnRelease = true };
            var right = new TransferrableObject { currentState = State.OnLeftArm, ChangeStateOnRelease = false };
            True(api.Attach(left)); True(api.Attach(right));
            equipment.leftHandHeldEquipment = left; equipment.rightHandHeldEquipment = right;
            Throws<InvalidOperationException>(() => api.SetEnabled(false));
            Equal(1, left.ReleaseCalls); Equal(1, right.ReleaseCalls);
            Equal(State.OnRightArm, left.currentState); Equal(State.OnLeftArm, right.currentState);
            False(api.Suspended); False(api.Enabled);
            api.Forget(left); api.Forget(right);
        });

        Run("remote and excluded held objects are not released during cleanup", () =>
        {
            HoldableApi api = Api(); EquipmentInteractor equipment = EquipmentInteractor.instance!;
            var left = new TransferrableObject { Local = false, currentState = State.InLeftHand };
            var right = new DecorativeItem { currentState = State.InRightHand };
            equipment.leftHandHeldEquipment = left; equipment.rightHandHeldEquipment = right;
            api.SetEnabled(false);
            Equal(0, left.ReleaseCalls); Equal(0, right.ReleaseCalls);
        });

        Run("method discovery includes genuine release overrides and Unity lifecycle hiding", () =>
        {
            HoldableApi api = Api();
            True(api.ReleaseTargets.Any(m => m.DeclaringType == typeof(TransferrableObject)));
            True(api.ReleaseTargets.Any(m => m.DeclaringType == typeof(OverrideHoldable)));
            False(api.ReleaseTargets.Any(m => m.DeclaringType == typeof(HiddenLifecycleHoldable)));
            False(api.ReleaseTargets.Any(m => m.DeclaringType == typeof(AbstractHoldable)));
            False(api.ReleaseTargets.Any(m => m.DeclaringType == typeof(UnrelatedType)));
            True(api.EnableTargets.Any(m => m.DeclaringType == typeof(HiddenLifecycleHoldable)));
            True(api.DisableTargets.Any(m => m.DeclaringType == typeof(OverrideHoldable)));
            True(api.DisableTargets.Any(m => m.DeclaringType == typeof(HiddenLifecycleHoldable)));
            Equal(api.ReleaseTargets.Count, api.ReleaseTargets.Distinct().Count());
        });

        Run("missing game contract is rejected before hooks could be installed", () =>
        {
            Assembly assembly = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("MissingGameContract"), AssemblyBuilderAccess.Run);
            Throws<NotSupportedException>(() => new HoldableApi(assembly));
        });

        Run("disabled-at-bind does not release unrelated held equipment", () =>
        {
            var item = new TransferrableObject { currentState = State.InLeftHand };
            EquipmentInteractor.instance!.leftHandHeldEquipment = item;
            var api = new HoldableApi(typeof(TransferrableObject).Assembly);
            False(api.Enabled);
            api.SetEnabled(false);
            Equal(0, item.ReleaseCalls); Equal(State.InLeftHand, item.currentState);
            False(api.ShouldKeep(item, null, EquipmentInteractor.instance.leftHand));
        });

        Console.WriteLine($"StickyHoldables contract checks: {passed} passed, {failed} failed; {assertions} assertions.");
        Console.WriteLine("Synthetic reflection-contract tests only; no game or uploaded DLL was loaded or executed.");
        return failed == 0 ? 0 : 1;
    }

    private static HoldableApi Api()
    {
        var api = new HoldableApi(typeof(TransferrableObject).Assembly);
        api.SetEnabled(true);
        return api;
    }

    private static void Run(string name, Action body)
    {
        EquipmentInteractor.instance = new EquipmentInteractor();
        CosmeticsController.instance = new CosmeticsController();
        try { body(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + "\n" + error); }
    }

    private static void True(bool value) { assertions++; if (!value) throw new Exception("Expected true"); }
    private static void False(bool value) { assertions++; if (value) throw new Exception("Expected false"); }
    private static void Same(object expected, object? actual)
    {
        assertions++; if (!ReferenceEquals(expected, actual)) throw new Exception("Expected same object identity");
    }
    private static void Equal<T>(T expected, T actual)
    {
        assertions++; if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Expected {expected}, got {actual}");
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        assertions++;
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
