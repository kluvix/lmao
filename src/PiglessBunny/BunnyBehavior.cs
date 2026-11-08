using System;

namespace PiglessBunny;

internal enum BunnyMode { Follow, Shoulder, LeftHand, RightHand }

// Distances and speeds are normalized to the player's scale before reaching
// this controller. It has no Unity/game dependencies and never sends input.
internal readonly struct BunnyInput
{
    internal readonly double Time;
    internal readonly bool Ready, LeftTracked, RightTracked, GripsAvailable, LeftGrip, RightGrip;
    internal readonly float LeftDistance, RightDistance, LeftSpeed, RightSpeed, HeadSpeed, ShoulderDistance;
    internal readonly bool AutoHand, AutoShoulder;

    internal BunnyInput(double time, bool ready, bool leftTracked, bool rightTracked,
        bool gripsAvailable, bool leftGrip, bool rightGrip, float leftDistance,
        float rightDistance, float leftSpeed, float rightSpeed, float headSpeed,
        float shoulderDistance, bool autoHand = true, bool autoShoulder = true)
    {
        Time = time; Ready = ready; LeftTracked = leftTracked; RightTracked = rightTracked;
        GripsAvailable = gripsAvailable; LeftGrip = leftGrip; RightGrip = rightGrip;
        LeftDistance = leftDistance; RightDistance = rightDistance;
        LeftSpeed = leftSpeed; RightSpeed = rightSpeed; HeadSpeed = headSpeed;
        ShoulderDistance = shoulderDistance; AutoHand = autoHand; AutoShoulder = autoShoulder;
    }
}

internal sealed class BunnyBehavior
{
    internal BunnyMode Mode { get; private set; } = BunnyMode.Follow;
    internal bool CarriedByGrip { get; private set; }
    private bool clockReady, leftGripSeeded, rightGripSeeded, previousLeft, previousRight;
    private bool leftOfferArmed = true, rightOfferArmed = true;
    private double lastTime, idleSince = -1, offerSince = -1, shoulderSince = -1, cooldownUntil;
    private BunnyMode? offeredHand;

    internal void Reset()
    {
        Mode = BunnyMode.Follow; CarriedByGrip = false; clockReady = leftGripSeeded = rightGripSeeded = false;
        leftOfferArmed = rightOfferArmed = true;
        lastTime = cooldownUntil = 0;
        idleSince = offerSince = shoulderSince = -1; offeredHand = null;
    }

    internal void Cycle(double time)
    {
        if (!Finite(time)) return;
        SetMode(Mode == BunnyMode.Follow ? BunnyMode.Shoulder : BunnyMode.Follow, time);
    }

    internal void RecallToShoulder(double time)
    {
        if (Finite(time)) SetMode(BunnyMode.Shoulder, time);
    }

    internal void Tick(BunnyInput input)
    {
        if (!input.Ready || !Finite(input.Time)) { Reset(); return; }
        // Tracking gaps/recenters must not turn a held button or stale dwell
        // timestamp into an accidental pickup/perch.
        if (!clockReady || input.Time < lastTime || input.Time - lastTime > 1)
        {
            idleSince = offerSince = shoulderSince = -1; offeredHand = null;
            leftGripSeeded = rightGripSeeded = false; clockReady = true;
        }
        lastTime = input.Time;
        if (!input.LeftTracked || Far(input.LeftDistance, .40f)) leftOfferArmed = true;
        if (!input.RightTracked || Far(input.RightDistance, .40f)) rightOfferArmed = true;
        bool leftPressed = false, rightPressed = false;
        if (input.GripsAvailable)
        {
            leftPressed = leftGripSeeded && input.LeftTracked && input.LeftGrip && !previousLeft;
            rightPressed = rightGripSeeded && input.RightTracked && input.RightGrip && !previousRight;
            previousLeft = input.LeftGrip; previousRight = input.RightGrip;
            leftGripSeeded = input.LeftTracked; rightGripSeeded = input.RightTracked;
        }
        else leftGripSeeded = rightGripSeeded = false;

        bool heldLeft = Mode == BunnyMode.LeftHand;
        bool heldRight = Mode == BunnyMode.RightHand;
        if (heldLeft || heldRight)
        {
            bool tracked = heldLeft ? input.LeftTracked : input.RightTracked;
            bool grip = heldLeft ? input.LeftGrip : input.RightGrip;
            if (!tracked) { SetMode(BunnyMode.Shoulder, input.Time); return; }
            if (CarriedByGrip && input.GripsAvailable && !grip)
            {
                if (heldLeft) leftOfferArmed = false; else rightOfferArmed = false;
                SetMode(Near(input.ShoulderDistance, .23f) ? BunnyMode.Shoulder : BunnyMode.Follow, input.Time);
                return;
            }
            if (!CarriedByGrip && (heldLeft ? leftPressed : rightPressed)) CarriedByGrip = true;
            if (!CarriedByGrip && Near(input.ShoulderDistance, .23f) && Still(heldLeft ? input.LeftSpeed : input.RightSpeed, .9f))
            {
                if (shoulderSince < 0) shoulderSince = input.Time;
                if (input.Time - shoulderSince >= .55) SetMode(BunnyMode.Shoulder, input.Time);
            }
            else shoulderSince = -1;
            return;
        }

        bool grabLeft = input.LeftTracked && leftPressed && Near(input.LeftDistance, .30f);
        bool grabRight = input.RightTracked && rightPressed && Near(input.RightDistance, .30f);
        if (grabLeft || grabRight)
        {
            bool left = grabLeft && (!grabRight || input.LeftDistance <= input.RightDistance);
            SetMode(left ? BunnyMode.LeftHand : BunnyMode.RightHand, input.Time);
            CarriedByGrip = true;
            return;
        }

        if (input.AutoHand && input.Time >= cooldownUntil)
        {
            bool left = input.LeftTracked && leftOfferArmed && (!input.GripsAvailable || !input.LeftGrip) &&
                        Near(input.LeftDistance, .22f) && Still(input.LeftSpeed, .65f);
            bool right = input.RightTracked && rightOfferArmed && (!input.GripsAvailable || !input.RightGrip) &&
                         Near(input.RightDistance, .22f) && Still(input.RightSpeed, .65f);
            BunnyMode? candidate = left && (!right || input.LeftDistance <= input.RightDistance) ? BunnyMode.LeftHand :
                right ? BunnyMode.RightHand : null;
            if (candidate == null) { offeredHand = null; offerSince = -1; }
            else if (candidate != offeredHand) { offeredHand = candidate; offerSince = input.Time; }
            else if (offerSince >= 0 && input.Time - offerSince >= .65)
            {
                SetMode(candidate.Value, input.Time);
                return;
            }
        }
        else { offeredHand = null; offerSince = -1; }

        if (Mode == BunnyMode.Follow && input.AutoShoulder && Still(input.HeadSpeed, .18f))
        {
            if (idleSince < 0) idleSince = input.Time;
            if (input.Time >= cooldownUntil && input.Time - idleSince >= 6)
                SetMode(BunnyMode.Shoulder, input.Time);
        }
        else idleSince = -1;
    }

    private void SetMode(BunnyMode mode, double time)
    {
        Mode = mode; CarriedByGrip = false;
        offeredHand = null; offerSince = idleSince = shoulderSince = -1;
        cooldownUntil = time + 1;
    }

    private static bool Near(float distance, float threshold) => !float.IsNaN(distance) && distance >= 0 && distance <= threshold;
    private static bool Far(float distance, float threshold) => !float.IsNaN(distance) && distance > threshold;
    private static bool Still(float speed, float threshold) => !float.IsNaN(speed) && speed >= 0 && speed <= threshold;
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
