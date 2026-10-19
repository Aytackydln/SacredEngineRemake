namespace Sacred.Network.Gameplay;

/// <summary>Participant-side requirements for sending the captured one-shot container request.
/// Target ownership remains with GameServer; a participant can activate a host-owned container.</summary>
public static class GoldContainerInteractionRequest
{
    public static GoldContainerActivationEvent Create(GoldWorldReplica world, byte playerSlot, uint reference)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (playerSlot == 0 || world.LocalPlayerSlot != playerSlot ||
            !world.Objects.TryGetValue(playerSlot, out var hero) || hero.Creature is null || hero.BaseState.Owner != playerSlot ||
            (hero.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0)
            throw new InvalidOperationException("Container interaction requires the active owned participant hero.");
        if (!world.Objects.TryGetValue(reference, out var target) || target.Container is null || target.Physical is null ||
            (target.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0 ||
            !world.Sectors.ContainsKey(target.BaseState.Sector) || hero.BaseState.Surface != target.BaseState.Surface)
            throw new InvalidOperationException("Container target is not active on confirmed terrain on the participant's surface.");
        if ((target.BaseState.Flags & GoldContainerActivationCodec.ActivatedFlag) != 0)
            throw new InvalidOperationException("This container is already opened or broken.");
        if (target.BaseState.TriggerReference != 0 &&
            (!world.WorldTriggers.Records.TryGetValue(target.BaseState.TriggerReference, out var trigger) || (trigger.State & 0xc000) != 0))
            throw new InvalidOperationException("Container has an unavailable or locked trigger.");
        return GoldContainerActivationCodec.Decode(GoldContainerActivationCodec.Create(reference, playerSlot, playerSlot));
    }
}
