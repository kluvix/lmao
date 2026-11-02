namespace Pigless.Interaction;

// A button fires once per contact. Slide away or withdraw before pressing again.
internal sealed class TouchLatch
{
    private int contact;
    internal bool Poll(int action)
    {
        bool fire = action != 0 && contact == 0;
        contact = action;
        return fire;
    }
    internal void Reset() => contact = 0;
}
