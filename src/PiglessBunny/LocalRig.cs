using System;
using System.Reflection;
using UnityEngine;

namespace PiglessBunny;

internal readonly struct RigFrame
{
    internal readonly object Identity;
    internal readonly Transform Head, PlayerRoot, TaggerRoot;
    internal readonly Transform? LeftHand, RightHand, RightShoulder;
    internal readonly bool GripsAvailable, LeftGrip, RightGrip;
    internal readonly float Scale;
    internal readonly int EnvironmentMask;

    internal RigFrame(object identity, Transform head, Transform playerRoot, Transform taggerRoot,
        Transform? leftHand, Transform? rightHand, bool gripsAvailable, bool leftGrip, bool rightGrip,
        float scale, int environmentMask, Transform? rightShoulder = null)
    {
        Identity = identity; Head = head; PlayerRoot = playerRoot; TaggerRoot = taggerRoot;
        LeftHand = leftHand; RightHand = rightHand; GripsAvailable = gripsAvailable;
        LeftGrip = leftGrip; RightGrip = rightGrip; Scale = scale; EnvironmentMask = environmentMask;
        RightShoulder = rightShoulder;
    }
}

// Only reads the game's explicitly local singletons. No game assemblies are
// loaded by this plugin and none of these objects or their fields are changed.
internal sealed class LocalRig
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private Assembly? game;
    private MemberInfo? playerInstance, taggerInstance, pollerInstance;
    private MemberInfo? camera, headCollider, scale, mask, staticMask;
    private MemberInfo? taggerLeft, taggerRight, offlineRig;
    private MemberInfo? rigIsOffline, bodyDock, rightBack;
    private MemberInfo? leftGripFloat, rightGripFloat, leftValid, rightValid;
    private MemberInfo? leftDeviceValid, rightDeviceValid, handTrackingActive;
    private MethodInfo? handFollower;
    private Type? pollerType;
    private object? lastPlayer, lastPoller;
    private bool bound, leftPressed, rightPressed;

    internal string Status { get; private set; } = "Waiting for local player.";

    internal bool TryRead(out RigFrame frame)
    {
        frame = default;
        try
        {
            if (!bound && !TryBind()) return false;
            object? player = Read(playerInstance, null), tagger = Read(taggerInstance, null);
            Transform? playerRoot = TransformOf(player), taggerRoot = TransformOf(tagger);
            if (!Usable(playerRoot) || !Usable(taggerRoot))
            {
                Status = "Waiting for local player.";
                return false;
            }

            // GTPlayer.mainCamera is the game's tracked headset camera. A
            // spectator/mod Camera.main is deliberately never used as a fallback.
            Transform? head = TransformOf(Read(camera, player));
            if (!Usable(head)) head = TransformOf(Read(headCollider, player));
            if (!Usable(head))
            {
                Status = "Waiting for tracked headset.";
                return false;
            }

            float playerScale = Read(scale, player) is float value ? value : 1f;
            if (!Finite(playerScale) || playerScale < .01f || playerScale > 100f)
            {
                Status = "Waiting for valid player scale.";
                return false;
            }

            if (!ReferenceEquals(player, lastPlayer))
            {
                lastPlayer = player; lastPoller = null;
                leftPressed = rightPressed = false;
            }

            Transform? left = GetHand(player!, tagger!, true);
            Transform? right = GetHand(player!, tagger!, false);
            object? poller = Read(pollerInstance, null);
            bool gripsAvailable = false;
            if (Alive(poller))
            {
                if (!ReferenceEquals(poller, lastPoller))
                {
                    lastPoller = poller;
                    leftPressed = rightPressed = false;
                }
                bool leftReady = HandReady(poller!, leftValid, leftDeviceValid);
                bool rightReady = HandReady(poller!, rightValid, rightDeviceValid);
                // Serialized hand transforms can remain active while the
                // controller is disconnected. Do not offer a stale hand pose.
                bool leftInput = false, rightInput = false;
                if (leftReady && left != null && Read(leftGripFloat, poller) is float l && Finite(l))
                {
                    leftPressed = Hysteresis(leftPressed, l);
                    leftInput = true;
                }
                if (rightReady && right != null && Read(rightGripFloat, poller) is float r && Finite(r))
                {
                    rightPressed = Hysteresis(rightPressed, r);
                    rightInput = true;
                }
                if (!leftInput) left = null;
                if (!rightInput) right = null;
                // A disconnected controller must not disable input from the
                // other hand. Invalid sides are explicitly untracked above.
                gripsAvailable = leftInput || rightInput;
            }
            else if (pollerType != null)
            {
                // During scene initialization, missing input is unavailable,
                // rather than a synthetic grip-release event at position zero.
                left = right = null;
                lastPoller = null;
            }

            int environmentMask = LayerValue(Read(mask, player)) ??
                                  LayerValue(Read(staticMask, null)) ?? Physics.DefaultRaycastLayers;
            object? localRig = Read(offlineRig, tagger);
            Transform? shoulder = null;
            if (Alive(localRig) && Read(rigIsOffline, localRig) is true)
            {
                object? dock = Read(bodyDock, localRig);
                if (Alive(dock)) shoulder = TransformOf(Read(rightBack, dock));
                if (!Usable(shoulder)) shoulder = null;
            }
            frame = new RigFrame(player!, head!, playerRoot!, taggerRoot!, left, right,
                gripsAvailable, leftPressed, rightPressed, playerScale, environmentMask, shoulder);
            Status = gripsAvailable ? "Local player ready." : "Local player ready; grip input unavailable.";
            return true;
        }
        catch (Exception error)
        {
            // Reflection can race scene teardown. Hide the pet for this frame
            // and retry fresh singleton/transform reads next frame.
            Status = "Waiting for local rig: " + error.GetBaseException().GetType().Name + ".";
            return false;
        }
    }

    private bool TryBind()
    {
        if (game == null)
        {
            foreach (Assembly candidate in AppDomain.CurrentDomain.GetAssemblies())
                if (candidate.GetName().Name == "Assembly-CSharp") { game = candidate; break; }
        }
        if (game == null) { Status = "Waiting for Gorilla Tag."; return false; }
        Type? player = game.GetType("GorillaLocomotion.GTPlayer", false);
        Type? tagger = game.GetType("GorillaTagger", false);
        if (player == null || tagger == null)
        {
            Status = "Unsupported local-player API.";
            return false;
        }
        playerInstance = Member(player, "Instance", Static) ?? Member(player, "_instance", Static);
        taggerInstance = Member(tagger, "Instance", Static) ?? Member(tagger, "_instance", Static);
        camera = Member(player, "mainCamera", Instance);
        headCollider = Member(player, "headCollider", Instance);
        scale = Member(player, "scale", Instance);
        mask = Member(player, "locomotionEnabledLayers", Instance);
        staticMask = Member(player, "LocomotionEnabledLayers", Static);
        taggerLeft = Member(tagger, "leftHandTransform", Instance);
        taggerRight = Member(tagger, "rightHandTransform", Instance);
        offlineRig = Member(tagger, "offlineVRRig", Instance);
        Type? rig = game.GetType("VRRig", false), dock = game.GetType("BodyDockPositions", false);
        if (rig != null)
        {
            rigIsOffline = Member(rig, "isOfflineVRRig", Instance);
            bodyDock = Member(rig, "myBodyDockPositions", Instance);
        }
        if (dock != null) rightBack = Member(dock, "rightBackTransform", Instance);
        handFollower = player.GetMethod("GetHandFollower", Instance, null, new[] { typeof(bool) }, null);
        if (handFollower != null && !typeof(Transform).IsAssignableFrom(handFollower.ReturnType)) handFollower = null;

        pollerType = game.GetType("ControllerInputPoller", false);
        if (pollerType != null)
        {
            pollerInstance = Member(pollerType, "instance", Static);
            leftGripFloat = Member(pollerType, "leftControllerGripFloat", Instance);
            rightGripFloat = Member(pollerType, "rightControllerGripFloat", Instance);
            leftValid = Member(pollerType, "LeftHandValid", Instance);
            rightValid = Member(pollerType, "RightHandValid", Instance);
            leftDeviceValid = Member(pollerType, "leftControllerIsValid", Instance);
            rightDeviceValid = Member(pollerType, "rightControllerIsValid", Instance);
            handTrackingActive = Member(pollerType, "handTrackingActive", Instance);
        }
        bound = playerInstance != null && taggerInstance != null && (camera != null || headCollider != null);
        if (!bound) Status = "Unsupported local-player fields.";
        return bound;
    }

    private Transform? GetHand(object player, object tagger, bool left)
    {
        Transform? hand = handFollower?.Invoke(player, new object[] { left }) as Transform;
        if (Usable(hand)) return hand;
        hand = TransformOf(Read(left ? taggerLeft : taggerRight, tagger));
        if (Usable(hand)) return hand;
        object? rig = Read(offlineRig, tagger);
        if (Alive(rig) && Read(Member(rig!.GetType(), "isOfflineVRRig", Instance), rig) is true)
        {
            hand = TransformOf(Read(Member(rig.GetType(), left ? "leftHandTransform" : "rightHandTransform", Instance), rig));
            if (Usable(hand)) return hand;
        }
        return null;
    }

    private bool HandReady(object poller, MemberInfo? valid, MemberInfo? deviceValid)
    {
        if (Read(valid, poller) is bool ready) return ready;
        if (Read(deviceValid, poller) is bool controllerReady)
            return controllerReady || Read(handTrackingActive, poller) is true;
        // Unknown readiness must not turn stale/default grip values into input.
        return false;
    }

    private static bool Hysteresis(bool down, float value) => down ? value > .55f : value >= .75f;
    private static int? LayerValue(object? value) => value is LayerMask layer ? layer.value : value as int?;
    private static MemberInfo? Member(Type type, string name, BindingFlags flags) =>
        (MemberInfo?)type.GetField(name, flags) ?? type.GetProperty(name, flags);
    private static object? Read(MemberInfo? member, object? instance) => member switch
    {
        FieldInfo field => field.GetValue(instance),
        PropertyInfo property => property.GetValue(instance, null),
        _ => null
    };
    private static Transform? TransformOf(object? value)
    {
        if (!Alive(value)) return null;
        if (value is Transform transform) return transform;
        if (value is Component component) return component.transform;
        return value is GameObject gameObject ? gameObject.transform : null;
    }
    private static bool Alive(object? value) => value != null && (!(value is UnityEngine.Object native) || native != null);
    private static bool Usable(Transform? transform)
    {
        if (transform == null || !transform.gameObject.activeInHierarchy) return false;
        Vector3 p = transform.position; Quaternion q = transform.rotation;
        return Finite(p.x) && Finite(p.y) && Finite(p.z) &&
               Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w);
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
