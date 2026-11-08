using System;
using UnityEngine;

namespace PiglessBunny;

internal sealed class BunnyController : IDisposable
{
    private readonly LocalRig localRig = new();
    private readonly BunnyBehavior behavior = new();
    private readonly RaycastHit[] hits = new RaycastHit[32];
    private BunnyModel? model;
    private RigFrame rig;
    private object? identity;
    private bool initialized, wasVisible, hadLeft, hadRight;
    private Vector3 lastHead, lastLeft, lastRight, flatForward = Vector3.forward, groundPosition;
    private Vector3 transitionFrom;
    private BunnyMode renderedMode;
    private double transitionStarted;
    private float hopPhase, blockedTime;

    internal string Status { get; private set; } = "Waiting for local player.";

    internal void Cycle(double time)
    {
        if (initialized) behavior.Cycle(time);
    }

    internal void Tick(float delta, double time, bool visible, float size, bool autoHand, bool autoShoulder)
    {
        if (!visible)
        {
            Status = "Bunny hidden. F7 to show.";
            Hide(); return;
        }
        if (!Finite(delta) || delta <= 0 || double.IsNaN(time) || double.IsInfinity(time))
        {
            Hide(); return;
        }
        if (!localRig.TryRead(out rig))
        {
            Status = localRig.Status; Hide(); return;
        }
        float dt = Mathf.Min(delta, .05f);
        float scale = rig.Scale;
        float visualScale = Mathf.Clamp(size, .5f, 1.5f) * scale;
        Vector3 forward = Vector3.ProjectOnPlane(rig.Head.forward, Vector3.up);
        if (forward.sqrMagnitude > .01f) flatForward = forward.normalized;
        Vector3 shoulder = Shoulder(scale);
        Vector3 desiredGround = rig.Head.position + Vector3.Cross(Vector3.up, flatForward) * (.55f * scale) - flatForward * (.55f * scale);

        model ??= new BunnyModel();
        model.Root.localScale = Vector3.one * visualScale;
        bool reset = !initialized || !ReferenceEquals(identity, rig.Identity) ||
                     Vector3.Distance(lastHead, rig.Head.position) > 6 * scale;
        if (reset)
        {
            behavior.Reset(); identity = rig.Identity; initialized = true;
            lastHead = rig.Head.position;
            hadLeft = hadRight = false; blockedTime = hopPhase = 0;
            if (TryGround(desiredGround, out Vector3 ground)) groundPosition = ground;
            else { groundPosition = shoulder; behavior.RecallToShoulder(time); }
            model.Root.SetPositionAndRotation(groundPosition, Quaternion.LookRotation(flatForward));
            renderedMode = behavior.Mode; transitionFrom = groundPosition; transitionStarted = time - 1;
        }
        model.SetVisible(true); wasVisible = true;

        bool leftTracked = rig.LeftHand != null && rig.LeftHand.gameObject.activeInHierarchy;
        bool rightTracked = rig.RightHand != null && rig.RightHand.gameObject.activeInHierarchy;
        float headSpeed = Vector3.Distance(lastHead, rig.Head.position) / (Mathf.Max(delta, .001f) * scale);
        float leftSpeed = leftTracked && hadLeft ? Vector3.Distance(lastLeft, rig.LeftHand!.position) / (Mathf.Max(delta, .001f) * scale) : float.PositiveInfinity;
        float rightSpeed = rightTracked && hadRight ? Vector3.Distance(lastRight, rig.RightHand!.position) / (Mathf.Max(delta, .001f) * scale) : float.PositiveInfinity;
        Vector3 petCenter = model.Root.position + Vector3.up * (.17f * visualScale);
        float leftDistance = leftTracked ? Vector3.Distance(petCenter, rig.LeftHand!.position) / scale : float.PositiveInfinity;
        float rightDistance = rightTracked ? Vector3.Distance(petCenter, rig.RightHand!.position) / scale : float.PositiveInfinity;
        Transform? holdingHand = behavior.Mode == BunnyMode.LeftHand ? rig.LeftHand : behavior.Mode == BunnyMode.RightHand ? rig.RightHand : null;
        float shoulderDistance = holdingHand != null ? Vector3.Distance(holdingHand.position, shoulder + Vector3.up * (.08f * visualScale)) / scale : float.PositiveInfinity;
        behavior.Tick(new BunnyInput(time, true, leftTracked, rightTracked, rig.GripsAvailable,
            rig.LeftGrip, rig.RightGrip, leftDistance, rightDistance, leftSpeed, rightSpeed,
            headSpeed, shoulderDistance, autoHand, autoShoulder));

        if (Vector3.Distance(model.Root.position, rig.Head.position) > 8 * scale)
        {
            behavior.RecallToShoulder(time);
            model.Root.position = shoulder;
            renderedMode = behavior.Mode; transitionStarted = time - 1;
        }

        if (renderedMode != behavior.Mode)
            BeginTransition(time, desiredGround);

        bool moving = false;
        Vector3 target;
        switch (behavior.Mode)
        {
            case BunnyMode.LeftHand: target = rig.LeftHand!.position + Vector3.up * (.04f * scale); break;
            case BunnyMode.RightHand: target = rig.RightHand!.position + Vector3.up * (.04f * scale); break;
            case BunnyMode.Shoulder: target = shoulder; break;
            default:
                target = Follow(desiredGround, scale, dt, time, out moving);
                break;
        }
        if (renderedMode != behavior.Mode)
        {
            // Follow can recall the bunny when a wall/ledge has no safe route.
            // Start a real hop transition instead of snapping to the shoulder.
            BeginTransition(time, desiredGround);
            target = shoulder;
            moving = false;
        }
        float transition = Mathf.Clamp01((float)((time - transitionStarted) / .38));
        Vector3 position = Vector3.Lerp(transitionFrom, target, Mathf.SmoothStep(0, 1, transition));
        if (transition < 1) position.y += Mathf.Sin(transition * Mathf.PI) * (.10f * scale);
        else if (moving)
        {
            hopPhase = Mathf.Repeat(hopPhase + dt * 2.8f, 1);
            position.y += Mathf.Sin(hopPhase * Mathf.PI) * (.085f * scale);
        }
        else hopPhase = 0;

        Vector3 facing = behavior.Mode == BunnyMode.Shoulder ? flatForward :
            behavior.Mode is BunnyMode.LeftHand or BunnyMode.RightHand ? rig.Head.position - position :
            moving ? target - model.Root.position : rig.Head.position - position;
        facing.y = 0;
        Quaternion rotation = facing.sqrMagnitude > .001f ? Quaternion.LookRotation(facing.normalized) : model.Root.rotation;
        model.Root.SetPositionAndRotation(position, Quaternion.Slerp(model.Root.rotation, rotation, 1 - Mathf.Exp(-9 * dt)));
        model.Animate((float)time, moving ? Mathf.Sin(hopPhase * Mathf.PI) : 0, moving ? 0 : 1);
        Status = behavior.Mode switch
        {
            BunnyMode.Follow => "Following. F6 shoulder; grip nearby to pick up.",
            BunnyMode.Shoulder => "On your shoulder. Offer a hand or grip to pick up; F6 follow.",
            _ => behavior.CarriedByGrip ? "Carried. Release grip to set down." : "On your hand. Grip/release to set down, or bring to shoulder."
        };
        lastHead = rig.Head.position; hadLeft = leftTracked; hadRight = rightTracked;
        if (leftTracked) lastLeft = rig.LeftHand!.position;
        if (rightTracked) lastRight = rig.RightHand!.position;
    }

    private Vector3 Shoulder(float scale)
    {
        // Prefer the local rig's real torso dock: the pet stays on the body
        // when you turn your head to look at it, rather than sliding out of view.
        if (rig.RightShoulder != null) return rig.RightShoulder.position + Vector3.up * (.06f * scale);
        return rig.Head.position + Vector3.Cross(Vector3.up, flatForward) * (.29f * scale) -
               Vector3.up * (.32f * scale) - flatForward * (.10f * scale);
    }

    private void BeginTransition(double time, Vector3 desiredGround)
    {
        transitionFrom = model!.Root.position; transitionStarted = time;
        if (behavior.Mode == BunnyMode.Follow)
        {
            // Setting a carried bunny down begins at the floor below the hand,
            // not at hand height (which would look like an impassable step).
            if (TryGround(transitionFrom, out Vector3 underHand)) groundPosition = underHand;
            else if (TryGround(desiredGround, out Vector3 nearPlayer)) groundPosition = nearPlayer;
            else { behavior.RecallToShoulder(time); groundPosition = Shoulder(rig.Scale); }
        }
        renderedMode = behavior.Mode;
        blockedTime = hopPhase = 0;
    }

    private Vector3 Follow(Vector3 desired, float scale, float dt, double time, out bool moving)
    {
        moving = false;
        if (!TryGround(desired, out Vector3 destination))
        {
            behavior.RecallToShoulder(time); return Shoulder(scale);
        }
        Vector3 horizontal = destination - groundPosition; horizontal.y = 0;
        if (horizontal.magnitude < .16f * scale && Mathf.Abs(destination.y - groundPosition.y) < .08f * scale)
        {
            groundPosition.y = destination.y;
            blockedTime = 0; return groundPosition;
        }
        Vector3 candidate = Vector3.MoveTowards(groundPosition, destination, 2.8f * scale * dt);
        if (TryGround(candidate, out Vector3 supported) && Mathf.Abs(supported.y - groundPosition.y) <= .55f * scale &&
            !Blocked(groundPosition + Vector3.up * (.14f * scale), supported + Vector3.up * (.14f * scale)))
        {
            groundPosition = supported; moving = true; blockedTime = 0;
        }
        else
        {
            blockedTime += dt;
            if (blockedTime >= .8f) behavior.RecallToShoulder(time);
        }
        return groundPosition;
    }

    private bool TryGround(Vector3 point, out Vector3 ground)
    {
        // Cast below head level so an overhead roof/branch cannot win as the
        // ground surface when spawning or setting the bunny down.
        Vector3 origin = new(point.x, rig.Head.position.y - .15f * rig.Scale, point.z);
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, 6 * rig.Scale, rig.EnvironmentMask, QueryTriggerInteraction.Ignore);
        float closest = float.PositiveInfinity;
        ground = default;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].distance >= closest || hits[i].point.y > rig.Head.position.y - .20f * rig.Scale ||
                hits[i].normal.y < .5f || !EnvironmentCollider(hits[i].collider)) continue;
            closest = hits[i].distance; ground = hits[i].point + Vector3.up * (.005f * rig.Scale);
        }
        return !float.IsPositiveInfinity(closest);
    }

    private bool Blocked(Vector3 start, Vector3 end)
    {
        Vector3 delta = end - start;
        if (delta.sqrMagnitude < .000001f) return false;
        int count = Physics.SphereCastNonAlloc(start, .07f * rig.Scale, delta.normalized, hits, delta.magnitude, rig.EnvironmentMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (EnvironmentCollider(hits[i].collider) && hits[i].normal.y < .5f) return true;
        return false;
    }

    private bool EnvironmentCollider(Collider collider) => collider != null && !collider.isTrigger &&
        !collider.transform.IsChildOf(rig.PlayerRoot) && !collider.transform.IsChildOf(rig.TaggerRoot);

    private void Hide()
    {
        model?.SetVisible(false);
        if (wasVisible || initialized) { behavior.Reset(); initialized = wasVisible = false; identity = null; }
    }

    internal void HideAfterError() => Hide();
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    public void Dispose()
    {
        model?.Dispose(); model = null;
        behavior.Reset(); initialized = wasVisible = false; identity = null;
    }
}
