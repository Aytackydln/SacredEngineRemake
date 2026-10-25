using Sacred.Network.Gameplay;
using Sacred.Particles;

namespace Sacred.World.Networking;

/// <summary>Ordinary foreign corpse presentation at Gold 54101B..541070 and 54140A..541570.
/// No reference deletion, resurrection, script execution or outgoing flags are performed here.</summary>
public static class SacredNetworkCorpseRules
{
    public static float CleanupDelay(SacredParticleQuality quality) => quality switch
    {
        SacredParticleQuality.High => 20,
        SacredParticleQuality.Medium => 5,
        _ => 2
    };

    public static bool CanDisappear(GoldReplicatedObject actor, uint localPlayer) => actor.IsDead &&
        actor.BaseState.ObjectReference != localPlayer &&
        (actor.BaseState.Flags & (GoldObjectLifecycleLayout.InactiveFlag | GoldObjectStateLayout.HiddenFlag)) == 0 &&
        actor.Creature is { } creature && (creature.Word200 & 0x40) == 0 &&
        (creature.BehaviorFlags & 0x2000) == 0 && (creature.Word2B7 & 8) == 0 &&
        creature.DialogIndex is not (> 0 and < 60000);
}
