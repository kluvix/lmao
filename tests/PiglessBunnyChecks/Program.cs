using PiglessBunny;

// Execute only our input/state controller. No Unity, game, plugin, or uploaded
// binary is loaded: physical poses, rendering and runtime input remain untested.
internal static class Program
{
    private static int passed, failed, assertions;

    private static int Main()
    {
        Run("left grip picks up on a fresh press and releases back to following", () =>
        {
            BunnyBehavior bunny = Manual(BunnyMode.LeftHand);
            Mode(bunny, BunnyMode.LeftHand); True(bunny.CarriedByGrip);
            bunny.Tick(Frame(.2, leftGrip: true, leftDistance: .1f));
            Mode(bunny, BunnyMode.LeftHand); True(bunny.CarriedByGrip);
            bunny.Tick(Frame(.3, leftDistance: .1f));
            Mode(bunny, BunnyMode.Follow); False(bunny.CarriedByGrip);
        });

        Run("right grip pickup and release are symmetric", () =>
        {
            BunnyBehavior bunny = Manual(BunnyMode.RightHand);
            Mode(bunny, BunnyMode.RightHand); True(bunny.CarriedByGrip);
            bunny.Tick(Frame(.2, rightDistance: .1f));
            Mode(bunny, BunnyMode.Follow); False(bunny.CarriedByGrip);
        });

        Run("simultaneous new presses choose the nearest reachable hand with a stable tie", () =>
        {
            foreach (var sample in new[] { (.2f, .1f, BunnyMode.RightHand), (.1f, .2f, BunnyMode.LeftHand), (.1f, .1f, BunnyMode.LeftHand) })
            {
                var bunny = new BunnyBehavior(); bunny.Tick(Frame(0, autoHand: false));
                bunny.Tick(Frame(.1, leftGrip: true, rightGrip: true, leftDistance: sample.Item1, rightDistance: sample.Item2));
                Mode(bunny, sample.Item3); True(bunny.CarriedByGrip);
            }
        });

        Run("a button already held at startup is seeded rather than treated as a press", () =>
        {
            var bunny = new BunnyBehavior();
            bunny.Tick(Frame(0, leftGrip: true, leftDistance: .1f));
            bunny.Tick(Frame(.7, leftGrip: true, leftDistance: .1f));
            Mode(bunny, BunnyMode.Follow);
            bunny.Tick(Frame(.8, leftDistance: .1f));
            bunny.Tick(Frame(.9, leftGrip: true, leftDistance: .1f));
            Mode(bunny, BunnyMode.LeftHand); True(bunny.CarriedByGrip);
        });

        Run("untracked and out-of-reach hands cannot pick up from a new press", () =>
        {
            foreach (bool tracked in new[] { false, true })
            {
                var bunny = new BunnyBehavior(); bunny.Tick(Frame(0, autoHand: false));
                bunny.Tick(Frame(.1, leftTracked: tracked, leftGrip: true, leftDistance: tracked ? .301f : .1f, autoHand: false));
                Mode(bunny, BunnyMode.Follow);
                bunny.Tick(Frame(.2, leftGrip: true, leftDistance: .1f, autoHand: false));
                Mode(bunny, BunnyMode.Follow);
            }
        });

        Run("manual pickup remains available with automatic hand perching disabled", () =>
        {
            var bunny = new BunnyBehavior(); bunny.Tick(Frame(0, leftDistance: .1f, autoHand: false));
            bunny.Tick(Frame(.7, leftDistance: .1f, autoHand: false)); Mode(bunny, BunnyMode.Follow);
            bunny.Tick(Frame(.8, leftGrip: true, leftDistance: .1f, autoHand: false));
            Mode(bunny, BunnyMode.LeftHand); True(bunny.CarriedByGrip);
        });

        Run("explicit new grip can pick up during the automatic-perch cooldown", () =>
        {
            var bunny = new BunnyBehavior(); bunny.Tick(Frame(0));
            bunny.Cycle(.1); Mode(bunny, BunnyMode.Shoulder);
            bunny.Tick(Frame(.2, leftGrip: true, leftDistance: .1f));
            Mode(bunny, BunnyMode.LeftHand); True(bunny.CarriedByGrip);
        });

        Run("release does not auto-regrab the same hand without withdrawal", () =>
        {
            BunnyBehavior bunny = Manual(BunnyMode.LeftHand);
            bunny.Tick(Frame(.2, leftDistance: .1f));
            foreach (double time in new[] { .7, 1.2, 1.7, 2.2, 2.7 })
            {
                bunny.Tick(Frame(time, leftDistance: .1f)); Mode(bunny, BunnyMode.Follow);
            }
        });

        Run("withdrawing after release rearms a fresh automatic offer dwell", () =>
        {
            BunnyBehavior bunny = Manual(BunnyMode.LeftHand);
            bunny.Tick(Frame(.2, leftDistance: .1f));
            bunny.Tick(Frame(.8, leftDistance: .1f));
            bunny.Tick(Frame(1.3, leftDistance: .41f));
            bunny.Tick(Frame(1.4, leftDistance: .1f));
            bunny.Tick(Frame(2.0, leftDistance: .1f)); Mode(bunny, BunnyMode.Follow);
            bunny.Tick(Frame(2.06, leftDistance: .1f));
            Mode(bunny, BunnyMode.LeftHand); False(bunny.CarriedByGrip);
        });

        Run("missing grip input does not fake the release of a carried bunny", () =>
        {
            BunnyBehavior bunny = Manual(BunnyMode.LeftHand);
            bunny.Tick(Frame(.2, gripsAvailable: false));
            bunny.Tick(Frame(.7, gripsAvailable: false));
            Mode(bunny, BunnyMode.LeftHand); True(bunny.CarriedByGrip);
            bunny.Tick(Frame(.8, leftGrip: true)); Mode(bunny, BunnyMode.LeftHand);
            bunny.Tick(Frame(.9)); Mode(bunny, BunnyMode.Follow);
        });

        Run("grip availability recovery seeds held buttons without phantom pickup", () =>
        {
            var bunny = new BunnyBehavior(); bunny.Tick(Frame(0, autoHand: false));
            bunny.Tick(Frame(.1, gripsAvailable: false, autoHand: false));
            bunny.Tick(Frame(.2, leftGrip: true, leftDistance: .1f, autoHand: false));
            Mode(bunny, BunnyMode.Follow);
            bunny.Tick(Frame(.3, leftDistance: .1f, autoHand: false));
            bunny.Tick(Frame(.4, leftGrip: true, leftDistance: .1f, autoHand: false));
            Mode(bunny, BunnyMode.LeftHand); True(bunny.CarriedByGrip);
        });

        Run("one working controller can pick up and release while the other is untracked", () =>
        {
            foreach (bool left in new[] { true, false })
            {
                var bunny = new BunnyBehavior();
                bunny.Tick(Frame(0, leftTracked: left, rightTracked: !left, autoHand: false));
                bunny.Tick(Frame(.1, leftTracked: left, rightTracked: !left,
                    leftGrip: left, rightGrip: !left, leftDistance: .1f, rightDistance: .1f, autoHand: false));
                Mode(bunny, left ? BunnyMode.LeftHand : BunnyMode.RightHand); True(bunny.CarriedByGrip);
                bunny.Tick(Frame(.2, leftTracked: left, rightTracked: !left, autoHand: false));
                Mode(bunny, BunnyMode.Follow); False(bunny.CarriedByGrip);
            }
        });

        Run("hand tracking recovery seeds its held grip while the other controller stays available", () =>
        {
            foreach (bool left in new[] { true, false })
            {
                var bunny = new BunnyBehavior(); bunny.Tick(Frame(0, autoHand: false));
                bunny.Tick(Frame(.1, leftTracked: !left, rightTracked: left, autoHand: false));
                bunny.Tick(Frame(.2, leftGrip: left, rightGrip: !left,
                    leftDistance: .1f, rightDistance: .1f, autoHand: false));
                Mode(bunny, BunnyMode.Follow); False(bunny.CarriedByGrip);
                bunny.Tick(Frame(.3, leftGrip: left, rightGrip: !left,
                    leftDistance: .1f, rightDistance: .1f, autoHand: false));
                Mode(bunny, BunnyMode.Follow);
                bunny.Tick(Frame(.4, leftDistance: .1f, rightDistance: .1f, autoHand: false));
                bunny.Tick(Frame(.5, leftGrip: left, rightGrip: !left,
                    leftDistance: .1f, rightDistance: .1f, autoHand: false));
                Mode(bunny, left ? BunnyMode.LeftHand : BunnyMode.RightHand); True(bunny.CarriedByGrip);
            }
        });

        Run("losing the carried hand recalls to shoulder while losing the other hand does not", () =>
        {
            BunnyBehavior bunny = Manual(BunnyMode.LeftHand);
            bunny.Tick(Frame(.2, rightTracked: false, leftGrip: true));
            Mode(bunny, BunnyMode.LeftHand); True(bunny.CarriedByGrip);
            bunny.Tick(Frame(.3, leftTracked: false, leftGrip: true));
            Mode(bunny, BunnyMode.Shoulder); False(bunny.CarriedByGrip);
        });

        Run("a still open hand requires the full automatic offer dwell", () =>
        {
            var bunny = new BunnyBehavior(); bunny.Tick(Frame(0, leftDistance: .1f));
            bunny.Tick(Frame(.649, leftDistance: .1f)); Mode(bunny, BunnyMode.Follow);
            bunny.Tick(Frame(.65, leftDistance: .1f));
            Mode(bunny, BunnyMode.LeftHand); False(bunny.CarriedByGrip);
        });

        Run("moving hands cannot accumulate an offer dwell", () =>
        {
            var bunny = new BunnyBehavior();
            bunny.Tick(Frame(0, leftDistance: .1f, leftSpeed: .7f));
            bunny.Tick(Frame(.6, leftDistance: .1f, leftSpeed: .7f)); Mode(bunny, BunnyMode.Follow);
            bunny.Tick(Frame(.7, leftDistance: .1f));
            bunny.Tick(Frame(1.3, leftDistance: .1f)); Mode(bunny, BunnyMode.Follow);
            bunny.Tick(Frame(1.36, leftDistance: .1f)); Mode(bunny, BunnyMode.LeftHand);
        });

        Run("switching the nearest offered hand restarts the offer dwell", () =>
        {
            var bunny = new BunnyBehavior(); bunny.Tick(Frame(0, leftDistance: .1f, rightDistance: .2f));
            bunny.Tick(Frame(.4, leftDistance: .2f, rightDistance: .1f));
            bunny.Tick(Frame(.7, leftDistance: .2f, rightDistance: .1f)); Mode(bunny, BunnyMode.Follow);
            bunny.Tick(Frame(1.04, leftDistance: .2f, rightDistance: .1f)); Mode(bunny, BunnyMode.Follow);
            bunny.Tick(Frame(1.06, leftDistance: .2f, rightDistance: .1f));
            Mode(bunny, BunnyMode.RightHand); False(bunny.CarriedByGrip);
        });

        Run("an already closed grip is not an automatic open-hand offer", () =>
        {
            var bunny = new BunnyBehavior();
            foreach (double time in new[] { 0, .65, 1.3 }) bunny.Tick(Frame(time, leftGrip: true, leftDistance: .1f));
            Mode(bunny, BunnyMode.Follow);
            bunny.Tick(Frame(1.4, leftDistance: .1f));
            bunny.Tick(Frame(2.0, leftDistance: .1f)); Mode(bunny, BunnyMode.Follow);
            bunny.Tick(Frame(2.06, leftDistance: .1f));
            Mode(bunny, BunnyMode.LeftHand); False(bunny.CarriedByGrip);
        });

        Run("gripping an automatic perch promotes it to deliberate carry until release", () =>
        {
            BunnyBehavior bunny = OfferedLeft();
            bunny.Tick(Frame(.7, leftGrip: true)); True(bunny.CarriedByGrip); Mode(bunny, BunnyMode.LeftHand);
            bunny.Tick(Frame(.8)); Mode(bunny, BunnyMode.Follow); False(bunny.CarriedByGrip);
        });

        Run("automatic shoulder return needs a stable shoulder dwell", () =>
        {
            BunnyBehavior bunny = OfferedLeft();
            bunny.Tick(Frame(.7, shoulderDistance: .1f));
            bunny.Tick(Frame(1.249, shoulderDistance: .1f)); Mode(bunny, BunnyMode.LeftHand);
            bunny.Tick(Frame(1.251, shoulderDistance: .1f));
            Mode(bunny, BunnyMode.Shoulder); False(bunny.CarriedByGrip);
        });

        Run("moving away from shoulder breaks the shoulder return dwell", () =>
        {
            BunnyBehavior bunny = OfferedLeft();
            bunny.Tick(Frame(.7, shoulderDistance: .1f));
            bunny.Tick(Frame(1.1, shoulderDistance: .3f));
            bunny.Tick(Frame(1.2, shoulderDistance: .1f));
            bunny.Tick(Frame(1.7, shoulderDistance: .1f)); Mode(bunny, BunnyMode.LeftHand);
            bunny.Tick(Frame(1.76, shoulderDistance: .1f)); Mode(bunny, BunnyMode.Shoulder);
        });

        Run("manual carry stays held near shoulder until grip release places it there", () =>
        {
            BunnyBehavior bunny = Manual(BunnyMode.LeftHand);
            foreach (double time in new[] { .2, .8, 1.4 }) bunny.Tick(Frame(time, leftGrip: true, shoulderDistance: .1f));
            Mode(bunny, BunnyMode.LeftHand); True(bunny.CarriedByGrip);
            bunny.Tick(Frame(1.5, shoulderDistance: .1f));
            Mode(bunny, BunnyMode.Shoulder); False(bunny.CarriedByGrip);
        });

        Run("disabling automatic hand perching clears partial offer time", () =>
        {
            var bunny = new BunnyBehavior(); bunny.Tick(Frame(0, leftDistance: .1f));
            bunny.Tick(Frame(.4, leftDistance: .1f, autoHand: false));
            bunny.Tick(Frame(.5, leftDistance: .1f));
            bunny.Tick(Frame(1.14, leftDistance: .1f)); Mode(bunny, BunnyMode.Follow);
            bunny.Tick(Frame(1.16, leftDistance: .1f)); Mode(bunny, BunnyMode.LeftHand);
        });

        Run("continuous head stillness perches at six seconds, never earlier", () =>
        {
            var bunny = new BunnyBehavior();
            for (int time = 0; time <= 5; time++) bunny.Tick(Frame(time, headSpeed: .1f));
            bunny.Tick(Frame(5.99, headSpeed: .1f)); Mode(bunny, BunnyMode.Follow);
            bunny.Tick(Frame(6, headSpeed: .1f)); Mode(bunny, BunnyMode.Shoulder);
        });

        Run("head movement restarts the full idle interval", () =>
        {
            var bunny = new BunnyBehavior();
            for (int time = 0; time <= 3; time++) bunny.Tick(Frame(time, headSpeed: 0));
            bunny.Tick(Frame(3.1, headSpeed: .2f));
            for (int time = 4; time <= 9; time++) bunny.Tick(Frame(time, headSpeed: 0));
            bunny.Tick(Frame(9.99, headSpeed: 0)); Mode(bunny, BunnyMode.Follow);
            bunny.Tick(Frame(10, headSpeed: 0)); Mode(bunny, BunnyMode.Shoulder);
        });

        Run("disabling automatic shoulder perching clears partial idle time", () =>
        {
            var bunny = new BunnyBehavior();
            for (int time = 0; time <= 5; time++) bunny.Tick(Frame(time, headSpeed: 0));
            bunny.Tick(Frame(5.5, headSpeed: 0, autoShoulder: false));
            for (int time = 6; time <= 11; time++) bunny.Tick(Frame(time, headSpeed: 0));
            Mode(bunny, BunnyMode.Follow);
            bunny.Tick(Frame(12, headSpeed: 0)); Mode(bunny, BunnyMode.Shoulder);
        });

        Run("a missing-frame gap cannot count as six seconds of observed stillness", () =>
        {
            var bunny = new BunnyBehavior();
            for (int time = 0; time <= 5; time++) bunny.Tick(Frame(time, headSpeed: 0));
            for (int time = 10; time <= 15; time++) bunny.Tick(Frame(time, headSpeed: 0));
            Mode(bunny, BunnyMode.Follow);
            bunny.Tick(Frame(16, headSpeed: 0)); Mode(bunny, BunnyMode.Shoulder);
        });

        Run("a clock recenter resets idle time instead of reusing the old timer", () =>
        {
            var bunny = new BunnyBehavior();
            for (int time = 0; time <= 5; time++) bunny.Tick(Frame(time, headSpeed: 0));
            for (int time = 2; time <= 7; time++) bunny.Tick(Frame(time, headSpeed: 0));
            Mode(bunny, BunnyMode.Follow);
            bunny.Tick(Frame(8, headSpeed: 0)); Mode(bunny, BunnyMode.Shoulder);
        });

        Run("gaps reset automatic hand dwell and seed rather than invent grip presses", () =>
        {
            var bunny = new BunnyBehavior(); bunny.Tick(Frame(0, leftDistance: .1f));
            bunny.Tick(Frame(.5, leftDistance: .1f));
            bunny.Tick(Frame(2, leftDistance: .1f));
            bunny.Tick(Frame(2.6, leftDistance: .1f)); Mode(bunny, BunnyMode.Follow);
            bunny.Tick(Frame(2.66, leftDistance: .1f)); Mode(bunny, BunnyMode.LeftHand);
            foreach (double resumedTime in new[] { 2.0, -.5 })
            {
                var resumed = new BunnyBehavior(); resumed.Tick(Frame(0, autoHand: false));
                resumed.Tick(Frame(resumedTime, leftGrip: true, leftDistance: .1f, autoHand: false));
                Mode(resumed, BunnyMode.Follow);
            }
        });

        Run("repeated timestamps do not advance automatic dwell timers", () =>
        {
            var bunny = new BunnyBehavior();
            for (int frame = 0; frame < 50; frame++) bunny.Tick(Frame(0, leftDistance: .1f, headSpeed: 0));
            Mode(bunny, BunnyMode.Follow); False(bunny.CarriedByGrip);
        });

        Run("readiness loss resets carried state and reseeds held input on return", () =>
        {
            BunnyBehavior bunny = Manual(BunnyMode.LeftHand);
            bunny.Tick(Frame(.2, ready: false)); Mode(bunny, BunnyMode.Follow); False(bunny.CarriedByGrip);
            bunny.Tick(Frame(.3, leftGrip: true, leftDistance: .1f, autoHand: false)); Mode(bunny, BunnyMode.Follow);
            bunny.Tick(Frame(.4, leftDistance: .1f, autoHand: false));
            bunny.Tick(Frame(.5, leftGrip: true, leftDistance: .1f, autoHand: false)); Mode(bunny, BunnyMode.LeftHand);
        });

        Run("nonfinite clocks reset state and cannot convert a held button into pickup", () =>
        {
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                BunnyBehavior bunny = Manual(BunnyMode.LeftHand);
                bunny.Tick(Frame(invalid, leftGrip: true)); Mode(bunny, BunnyMode.Follow); False(bunny.CarriedByGrip);
                bunny.Tick(Frame(1, leftGrip: true, leftDistance: .1f, autoHand: false)); Mode(bunny, BunnyMode.Follow);
            }
        });

        Run("F6 cycle, shoulder recall and reset leave consistent carrying state", () =>
        {
            var bunny = new BunnyBehavior(); bunny.Cycle(0); Mode(bunny, BunnyMode.Shoulder);
            bunny.Cycle(.1); Mode(bunny, BunnyMode.Follow);
            bunny = Manual(BunnyMode.RightHand); bunny.Cycle(.2); Mode(bunny, BunnyMode.Follow); False(bunny.CarriedByGrip);
            bunny.RecallToShoulder(.3); Mode(bunny, BunnyMode.Shoulder);
            bunny.Reset(); Mode(bunny, BunnyMode.Follow); False(bunny.CarriedByGrip);
            bunny.Tick(Frame(1, rightGrip: true, rightDistance: .1f, autoHand: false)); Mode(bunny, BunnyMode.Follow);
        });

        Run("invalid control clocks cannot change the mode or clear manual carry", () =>
        {
            BunnyBehavior bunny = Manual(BunnyMode.LeftHand);
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                bunny.Cycle(invalid); bunny.RecallToShoulder(invalid);
                Mode(bunny, BunnyMode.LeftHand); True(bunny.CarriedByGrip);
            }
        });

        Run("invalid distances cannot trigger automatic or manual pickup or shoulder placement", () =>
        {
            foreach (float invalid in InvalidMetrics)
            {
                var bunny = new BunnyBehavior(); bunny.Tick(Frame(0, leftDistance: invalid, rightDistance: invalid));
                bunny.Tick(Frame(.1, leftGrip: true, rightGrip: true, leftDistance: invalid, rightDistance: invalid));
                bunny.Tick(Frame(.8, leftDistance: invalid, rightDistance: invalid)); Mode(bunny, BunnyMode.Follow);
                bunny = Manual(BunnyMode.LeftHand);
                bunny.Tick(Frame(.2, shoulderDistance: invalid)); Mode(bunny, BunnyMode.Follow);
            }
        });

        Run("invalid hand or head speeds never accumulate automatic perching time", () =>
        {
            foreach (float invalid in InvalidMetrics)
            {
                var bunny = new BunnyBehavior();
                for (int time = 0; time <= 8; time++)
                    bunny.Tick(Frame(time, leftDistance: .1f, rightDistance: .1f, leftSpeed: invalid, rightSpeed: invalid, headSpeed: invalid));
                Mode(bunny, BunnyMode.Follow); False(bunny.CarriedByGrip);
            }
        });

        Console.WriteLine($"PiglessBunny behavior checks: {passed} passed, {failed} failed; {assertions} assertions.");
        Console.WriteLine("Pure multi-frame behavior tests only; no game, Unity, plugin or uploaded DLL was loaded or executed.");
        return failed == 0 ? 0 : 1;
    }

    private static readonly float[] InvalidMetrics = { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1 };

    private static BunnyBehavior Manual(BunnyMode hand)
    {
        var bunny = new BunnyBehavior(); bunny.Tick(Frame(0, autoHand: false));
        bunny.Tick(Frame(.1, leftGrip: hand == BunnyMode.LeftHand, rightGrip: hand == BunnyMode.RightHand,
            leftDistance: .1f, rightDistance: .1f, autoHand: false));
        return bunny;
    }

    private static BunnyBehavior OfferedLeft()
    {
        var bunny = new BunnyBehavior(); bunny.Tick(Frame(0, leftDistance: .1f));
        bunny.Tick(Frame(.65, leftDistance: .1f)); return bunny;
    }

    private static BunnyInput Frame(double time, bool ready = true, bool leftTracked = true,
        bool rightTracked = true, bool gripsAvailable = true, bool leftGrip = false, bool rightGrip = false,
        float leftDistance = 1, float rightDistance = 1, float leftSpeed = 0, float rightSpeed = 0,
        float headSpeed = 1, float shoulderDistance = 1, bool autoHand = true, bool autoShoulder = true) =>
        new(time, ready, leftTracked, rightTracked, gripsAvailable, leftGrip, rightGrip,
            leftDistance, rightDistance, leftSpeed, rightSpeed, headSpeed, shoulderDistance, autoHand, autoShoulder);

    private static void Run(string name, Action check)
    {
        try { check(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + "\n" + error); }
    }

    private static void Mode(BunnyBehavior bunny, BunnyMode expected)
    {
        assertions++;
        if (bunny.Mode != expected) throw new InvalidOperationException($"Expected {expected}, got {bunny.Mode}.");
    }
    private static void True(bool value) { assertions++; if (!value) throw new InvalidOperationException("Expected true."); }
    private static void False(bool value) { assertions++; if (value) throw new InvalidOperationException("Expected false."); }
}
