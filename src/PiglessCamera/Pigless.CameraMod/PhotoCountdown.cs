using System;
namespace Pigless.CameraMod;
internal sealed class PhotoCountdown
{
    private double due;
    internal bool Pending { get; private set; }
    internal bool Start(double now)
    {
        if (Pending || double.IsNaN(now) || double.IsInfinity(now)) return false;
        due = now + 2.0;
        Pending = true;
        return true;
    }
    internal int Remaining(double now) => Pending ? Math.Max(0, (int)Math.Ceiling(due - now)) : 0;
    internal bool Poll(double now)
    {
        if (!Pending || now < due || double.IsNaN(now)) return false;
        Pending = false;
        return true;
    }
    internal void Cancel() => Pending = false;
}
