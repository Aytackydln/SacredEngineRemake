namespace Sacred.Network.Gameplay;

internal static class GoldCombatReducer
{
    public static bool Apply(Dictionary<uint, GoldReplicatedObject> objects, GoldCombatReplica combat, GoldCombatEvent entry)
    {
        if (entry is GoldCreatureTextEvent) entry = GoldCreatureTextCodec.Decode(entry.Message);
        if (!objects.TryGetValue(entry.ObjectReference, out var actor) || actor.Creature is null ||
            (actor.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0) return false;
        // The original receive guards check existence, type and active state, without an ownership filter.
        GoldCombatEvent owned = entry switch
        {
            GoldCreatureTextEvent text => text with { ReceiptPosition = new(actor.BaseState.X, actor.BaseState.Y, actor.BaseState.Surface, actor.BaseState.Sector) },
            GoldCreatureSpecialMoveEvent => GoldSpecialMoveCodec.Decode(entry.Message),
            GoldCreatureCastEvent => GoldCastCodec.Decode(entry.Message),
            GoldCreatureSleepEvent => GoldCreatureSleepCodec.Decode(entry.Message),
            _ => GoldCombatCodec.Decode(entry.Message)
        };
        var current = actor.CurrentHitPoints ?? actor.Values?.CurrentHitPoints;
        combat.Add(owned, actor.InstanceVersion, owned is GoldCreatureHealthEvent
            ? actor.ReportedHitPoints ?? current : current, actor);
        objects[entry.ObjectReference] = owned switch
        {
            GoldCreatureSleepEvent sleep => actor with
            {
                Sleep = sleep,
                Creature = actor.Creature.WithSleepingFlag(),
                Movement = null, MovementVersion = actor.MovementVersion + 1,
                Attack = null, SpecialMove = null, Cast = null, AttackVersion = actor.AttackVersion + 1
            },
            GoldCreatureHealthEvent health => actor with
            {
                Death = health.HitPoints > 0 ? null : actor.Death,
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
                Attack = attack, SpecialMove = null, Cast = null, AttackVersion = actor.AttackVersion + 1
            },
            GoldCreatureSpecialMoveEvent special => actor with
            {
                Movement = null, MovementVersion = actor.MovementVersion + 1,
                Attack = null, Cast = null, SpecialMove = MergeSpecialMove(actor.SpecialMove, special), AttackVersion = actor.AttackVersion + 1
            },
            GoldCreatureCastEvent cast => actor with
            {
                Movement = null, MovementVersion = actor.MovementVersion + 1,
                Attack = null, SpecialMove = null, Cast = ApplyCast(cast), AttackVersion = actor.AttackVersion + 1
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

    private static GoldCreatureCastEvent ApplyCast(GoldCreatureCastEvent incoming)
    {
        var bank = new byte[GoldCastLayout.MaximumActionBytes];
        incoming.ActionState.AsSpan(0, Math.Min(bank.Length, incoming.ActionState.Length)).CopyTo(bank);
        return incoming with { ActionState = bank };
    }

    private static GoldCreatureSpecialMoveEvent MergeSpecialMove(GoldCreatureSpecialMoveEvent? previous,
        GoldCreatureSpecialMoveEvent incoming)
    {
        var bank = new byte[GoldSpecialMoveLayout.MaximumActionBytes];
        if (previous is not null) previous.ActionState.AsSpan(0, Math.Min(bank.Length, previous.ActionState.Length)).CopyTo(bank);
        incoming.ActionState.AsSpan(0, Math.Min(bank.Length, incoming.ActionState.Length)).CopyTo(bank);
        return incoming with { ActionState = bank };
    }
}
