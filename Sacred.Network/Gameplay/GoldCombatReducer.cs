namespace Sacred.Network.Gameplay;

internal static class GoldCombatReducer
{
    public static bool Apply(Dictionary<uint, GoldReplicatedObject> objects, GoldCombatReplica combat, GoldCombatEvent entry)
    {
        if (!objects.TryGetValue(entry.ObjectReference, out var actor) || actor.Creature is null ||
            (actor.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0) return false;
        // The original receive guards check existence, type and active state, without an ownership filter.
        var owned = GoldCombatCodec.Decode(entry.Message);
        var current = actor.CurrentHitPoints ?? actor.Values?.CurrentHitPoints;
        combat.Add(owned, actor.InstanceVersion, owned is GoldCreatureHealthEvent
            ? actor.ReportedHitPoints ?? current : current);
        objects[entry.ObjectReference] = owned switch
        {
            GoldCreatureHealthEvent health => actor with
            {
                CurrentHitPoints = health.HitPoints,
                ReportedHitPoints = health.HitPoints,
                Values = actor.Values?.WithCurrentHitPoints(health.HitPoints)
            },
            GoldCreatureAttackEvent attack => actor with
            {
                BaseState = actor.BaseState with { X = attack.Position.X, Y = attack.Position.Y,
                    Sector = unchecked((ushort)attack.Position.Sector), Surface = unchecked((byte)attack.Position.Surface) },
                FacingBits = attack.FacingBits, Physical = actor.Physical?.WithDirection(GoldPhysicalFacing.ToDirection(attack.FacingBits)),
                Movement = null, MovementVersion = actor.MovementVersion + 1,
                PositionOpcode = attack.Message.Opcode, PositionVersion = actor.PositionVersion + 1,
                Attack = attack, AttackVersion = actor.AttackVersion + 1
            },
            // Gold 0x53205A subtracts the received ushort, then 0x128 independently assigns TP[2].
            // Keep the last absolute report separate so a damage correction is not classified as a heal.
            GoldCreatureDamageEvent damage when current is { } hp && hp != 0 => actor with
            {
                CurrentHitPoints = unchecked(hp - damage.Damage),
                Values = actor.Values?.WithCurrentHitPoints(unchecked(hp - damage.Damage))
            },
            _ => actor
        };
        return true;
    }
}
