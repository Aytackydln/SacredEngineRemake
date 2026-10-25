using Sacred.Core.Pak.Items;

namespace Sacred.Network.Gameplay;

/// <summary>Combines full arrival layers with independent runtime state for the same object lifetime.</summary>
internal static class GoldReplicatedObjectFactory
{
    public static GoldReplicatedObject FromEvent(GoldObjectEvent obj, GoldReplicatedObject? previous,
        SacredItemCategory category, long version)
    {
        var basis = obj.BaseState;
        if (previous?.BaseState.ObjectType != basis.ObjectType ||
            previous is not null && (previous.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0) previous = null;
        var incomingFx = obj.FxState;
        return new(basis, obj.CreatureState ?? previous?.Creature,
            obj.ItemState ?? previous?.Item, obj.CreatureState is not null ? null : previous?.Movement)
        {
            InstanceVersion = previous?.InstanceVersion ?? version,
            ItemValuesVersion = obj.ItemState is not null ? version : previous?.ItemValuesVersion ?? 0,
            Container = obj.ItemState is not null && category == SacredItemCategory.Container
                ? GoldContainerState.FromPayload(obj.ObjectState) : previous?.Container,
            Physical = obj.PhysicalState ?? previous?.Physical,
            Fx = incomingFx ?? previous?.Fx,
            FxReloadVersion = (previous?.FxReloadVersion ?? 0) + (incomingFx is not null ? 1 : 0),
            // Every full object arrival supplies a newer base position. It supersedes a deferred
            // special-FX relocation while preserving particle runtime fields absent from its stream.
            SpecialFx = previous?.SpecialFx is { } runtimeFx ? runtimeFx with { RelocationPending = false } : null,
            Values = obj.CreatureTail?.Values ?? previous?.Values,
            CurrentHitPoints = obj.CreatureTail?.Values?.CurrentHitPoints ?? previous?.CurrentHitPoints,
            ReportedHitPoints = obj.CreatureTail?.Values?.CurrentHitPoints ?? previous?.ReportedHitPoints,
            Death = obj.CreatureTail?.Values?.CurrentHitPoints is > 0 ? null : previous?.Death,
            DeathVersion = previous?.DeathVersion ?? 0,
            Attack = obj.CreatureState is not null ? null : previous?.Attack,
            SpecialMove = obj.CreatureState is not null ? null : previous?.SpecialMove,
            Cast = obj.CreatureState is not null ? null : previous?.Cast,
            Sleep = obj.CreatureState is not null ? null : previous?.Sleep,
            TimedControl = obj.CreatureState is not null ? null : previous?.TimedControl,
            LinkedCreatures = obj.CreatureTail is { } tail ? GoldCreatureLinkState.FromPayload(tail.Records) : previous?.LinkedCreatures,
            AttackVersion = (previous?.AttackVersion ?? 0) + (obj.CreatureState is not null ? 1 : 0),
            FacingBits = obj.PhysicalState is { } physical ? GoldPhysicalFacing.FromDirection(physical.Vector70) : basis.Word28,
            PositionOpcode = obj.Message.Opcode,
            FlightVersion = (previous?.FlightVersion ?? 0) + 1,
            PositionVersion = (previous?.PositionVersion ?? 0) + 1,
            MovementVersion = (previous?.MovementVersion ?? 0) + 1,
            SnapshotMovementVersion = obj.CreatureState is not null ? (previous?.MovementVersion ?? 0) + 1 : previous?.SnapshotMovementVersion ?? 0,
            MountPartnerReference = obj.CreatureTail?.MountPartnerReference ?? previous?.MountPartnerReference
        };
    }
}
