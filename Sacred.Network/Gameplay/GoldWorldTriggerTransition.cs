namespace Sacred.Network.Gameplay;

/// <summary>Receive-side cTrigger set/clear (Gold 0x415F00/0x416280, publish=false).
/// Returns false before an inventory query or scheduled world callback would be needed.</summary>
public static class GoldWorldTriggerTransition
{
    public static bool TryApply(GoldWorldTriggerState trigger, uint mask, bool set, out GoldWorldTriggerState result)
    {
        var state = trigger.State;
        if (set)
        {
            if ((mask & 0x8000) != 0) state |= 0x8000;
            if (!((state & 0x8000) != 0 && (mask & 1) != 0) && (state & 0x4000) == 0)
            {
                if ((trigger.Flags & 0x44) != 0)
                {
                    if (state == 0 && (mask & 1) != 0) { result = trigger; return false; }
                }
                else state |= unchecked((ushort)mask);
            }
        }
        else
        {
            if ((mask & 0x8000) != 0) state &= 0x7FFF;
            if ((mask & 0x4000) != 0) state &= 0xBFFF;
            if ((state & 0xC000) == 0)
            {
                if ((trigger.Flags & 0x44) != 0)
                {
                    if ((state & mask & 1) != 0) { result = trigger; return false; }
                }
                else state &= unchecked((ushort)~mask);
            }
        }
        result = trigger with { State = state };
        return true;

    }
}
