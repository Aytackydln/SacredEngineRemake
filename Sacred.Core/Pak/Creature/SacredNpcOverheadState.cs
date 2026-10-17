namespace Sacred.Core.Pak.Creature;

/// <summary>Gold render gate 0x599F2E and dialog selector 0x499E90.
/// Behavior and simulation state are independent of an actor's heading.</summary>
public readonly record struct SacredNpcOverheadState(uint ObjectFlags, uint BehaviorFlags, uint ServiceFlags,
    uint DialogIndex, ushort SimulationState)
{
    public const uint DialogObjectFlag = 0x80000;
    public const uint SimulationBehaviorMask = 0x1006DCF8;
    public const int SimulationStateCount = 38;

    public uint? Select(bool alt, bool templateDialog, bool animal, IReadOnlyList<int> simulationIcons,
        Func<uint, uint?> dialogIcon)
    {
        if ((ObjectFlags & 0x10) != 0) return null;
        if ((ObjectFlags & DialogObjectFlag) != 0 || templateDialog)
        {
            // Native checks this sentinel before service flags.
            if (DialogIndex >= 160000) return 4;
            if ((ServiceFlags & 0x1000) != 0) return 0x20;
            if ((ServiceFlags & 0x2000) != 0) return 0x21;
            if ((ServiceFlags & 0x4000) != 0) return 0x22;
            if ((ServiceFlags & 0x4000000) != 0) return 0x23;
            return dialogIcon(DialogIndex) ?? 0;
        }
        if (!alt || animal || (BehaviorFlags & SimulationBehaviorMask) == 0 || SimulationState >= simulationIcons.Count) return null;
        var icon = simulationIcons[SimulationState];
        return icon is > 0 and <= 0x5A ? (uint)icon : null;
    }
}
