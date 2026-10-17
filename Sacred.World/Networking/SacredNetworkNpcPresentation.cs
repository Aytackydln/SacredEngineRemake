using Sacred.Core.Pak.Creature;
using Sacred.Network.Gameplay;

namespace Sacred.World.Networking;

/// <summary>Locally derived presentation of existing NPC replicas. Authoritative wire bytes stay unchanged.</summary>
public static class SacredNetworkNpcPresentation
{
    public static SacredNpcOverheadState OverheadState(GoldReplicatedObject actor, GoldReplicatedObject hero)
    {
        var creature = actor.Creature ?? throw new ArgumentException("NPC presentation requires a creature.", nameof(actor));
        var state = new SacredNpcOverheadState(actor.BaseState.Flags, creature.BehaviorFlags, creature.ServiceFlags,
            creature.DialogIndex, creature.SimulationState);
        if (state.SimulationState != 0 || actor.BaseState.ObjectReference <= 16 || actor.Movement is not null ||
            (state.ObjectFlags & (0x10 | 0x100000)) != 0 || (state.ServiceFlags & 0x40000) != 0 ||
            actor.Values is not { CurrentHitPoints: > 0 }) return state;
        var distance = SacredNpcDistance.Between(actor.BaseState.X, actor.BaseState.Y, hero.BaseState.X, hero.BaseState.Y);
        return state with { SimulationState = SacredNpcClientRules.InitialIdleState(state.BehaviorFlags, distance) };
    }
}
