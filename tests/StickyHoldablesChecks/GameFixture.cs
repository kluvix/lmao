// A synthetic contract model, not recovered game code. Nothing in these checks
// loads the game, Unity, the mod plugin, or any user-uploaded DLL.
using UnityEngine;
using UnityEngine.XR;

public class DropZone { }
public class InteractionPoint { }

public class HoldableObject
{
    public virtual bool OnRelease(DropZone? zone, GameObject hand) => true;
    public virtual void OnHover(InteractionPoint point, GameObject hand) { }
}

public class TransferrableObject : HoldableObject
{
    // Deliberately different from the game's numeric values: the adapter must
    // discover named states rather than assume a particular enum layout.
    public enum PositionState
    {
        None = 0, OnLeftArm = 15, OnRightArm = 29,
        InLeftHand = 73, InRightHand = 86, OnChest = 103
    }

    public PositionState currentState = PositionState.OnLeftArm;
    public bool isSceneObject;
    public bool Local = true, Mine = true;
    public bool ChangeStateOnRelease = true, ThrowOnRelease;
    public int ReleaseCalls;
    public Action<DropZone?, GameObject>? ReleaseObserver;

    public virtual bool IsLocalObject() => Local;
    public virtual bool IsMyItem() => Mine;
    internal virtual void OnEnable() { }
    internal virtual void OnDisable() { }
    public override void OnHover(InteractionPoint point, GameObject hand) { }

    public override bool OnRelease(DropZone? zone, GameObject hand)
    {
        ReleaseCalls++;
        ReleaseObserver?.Invoke(zone, hand);
        if (ThrowOnRelease) throw new InvalidOperationException("Synthetic release failure");
        if (ChangeStateOnRelease)
        {
            currentState = PositionState.OnChest;
            EquipmentInteractor? equipment = EquipmentInteractor.instance;
            if (equipment != null)
            {
                if (ReferenceEquals(equipment.leftHandHeldEquipment, this)) equipment.leftHandHeldEquipment = null;
                if (ReferenceEquals(equipment.rightHandHeldEquipment, this)) equipment.rightHandHeldEquipment = null;
            }
        }
        return true;
    }
}

public class Slingshot : TransferrableObject { }
public class ThrowableBug : TransferrableObject { }
public class ThrowableSetDressing : TransferrableObject { }
public class DecorativeItem : TransferrableObject { }
public class DerivedSlingshot : Slingshot { }
public class DerivedThrowableBug : ThrowableBug { }
public class DerivedThrowableSetDressing : ThrowableSetDressing { }
public class DerivedDecorativeItem : DecorativeItem { }

public class EqualItem : TransferrableObject
{
    public override bool Equals(object? obj) => obj is EqualItem;
    public override int GetHashCode() => 1;
}

public class OverrideHoldable : TransferrableObject
{
    public override bool OnRelease(DropZone? zone, GameObject hand) => base.OnRelease(zone, hand);
    public override void OnHover(InteractionPoint point, GameObject hand) => base.OnHover(point, hand);
    internal override void OnEnable() { }
    internal override void OnDisable() { }
}

public class HiddenLifecycleHoldable : TransferrableObject
{
    public new void OnEnable() { }
    public new void OnDisable() { }
    public new bool OnRelease(DropZone? zone, GameObject hand) => true;
    public new void OnHover(InteractionPoint point, GameObject hand) { }
}

public abstract class AbstractHoldable : TransferrableObject
{
    public abstract override bool OnRelease(DropZone? zone, GameObject hand);
    public abstract override void OnHover(InteractionPoint point, GameObject hand);
}

public class UnrelatedType
{
    public bool OnRelease(DropZone? zone, GameObject hand) => true;
    public void OnHover(InteractionPoint point, GameObject hand) { }
    public void OnEnable() { }
}

public class EquipmentInteractor
{
    public static EquipmentInteractor? instance;
    public object? leftHandHeldEquipment, rightHandHeldEquipment;
    public GameObject leftHand = new(), rightHand = new();
    public bool GetIsHolding(XRNode node) =>
        (node == XRNode.LeftHand ? leftHandHeldEquipment : rightHandHeldEquipment) != null;
}

namespace GorillaNetworking
{
    public class CosmeticsController
    {
        public static CosmeticsController? instance;
        public sealed class CosmeticSet { }
        public sealed class CosmeticItem { }
        public enum CosmeticSlots { LeftHand, RightHand }
        public void ApplyCosmeticItemToSet(CosmeticSet set, CosmeticItem item,
            bool first, bool second, List<CosmeticSlots> slots) { }
    }
}

namespace UnityEngine
{
    public class GameObject
    {
        // Equality is deliberately broad; releasing hands must use identity.
        public override bool Equals(object? obj) => obj is GameObject;
        public override int GetHashCode() => 1;
    }
}

namespace UnityEngine.XR
{
    public enum XRNode { Head = 3, LeftHand = 27, RightHand = 42 }
}
