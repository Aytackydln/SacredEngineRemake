namespace Sacred.Core.Pak.Creature;

/// <summary>Client presentation rules from Gold 0x4EE030 / 0x4EF150 and 0x54646C.
/// These do not run NPC movement, conversations, combat or quest callbacks.</summary>
public static class SacredNpcClientRules
{
    /// <summary>Initial states written by the deterministic follow, guard and quest branches.
    /// A zero server snapshot does not contain the client's locally advanced simulation state.
    /// Preserve higher-priority behavior dispatch rather than treating flags as independent roles.</summary>
    public static ushort InitialIdleState(uint behavior, uint distanceToHero)
    {
        if ((behavior & 4) != 0) return 1;
        if ((behavior & (0x100 | 8 | 0x8000 | 0x10000000 | 0x10 | 0x20 | 0x40 | 0x80)) != 0) return 0;
        // Guard writes state 5 only within 1500 native units. Its later threat branch can replace it with 4.
        if ((behavior & 0x4000) != 0) return distanceToHero <= 1500 ? (ushort)5 : (ushort)0;
        if ((behavior & 0x400) != 0) return 0;
        return (behavior & (0x800 | 0x1000)) != 0 ? (ushort)6 : (ushort)0;
    }

    /// <summary>Local idle-facing eligibility. Ordinary dialogs use Gold 0x5464C4..0x546565.
    /// Callers give movement and active conversation targets priority.</summary>
    public static bool FacesLocalHero(uint reference, SacredNpcOverheadState state, bool horse) =>
        reference > 16 && state.DialogIndex is > 0 and < 100000 && !horse &&
        (state.ObjectFlags & (0x10 | 2 | 0x100000)) == 0 && (state.ServiceFlags & 0x40000) == 0 &&
        (state.BehaviorFlags & SacredNpcOverheadState.SimulationBehaviorMask) == 0;
}
