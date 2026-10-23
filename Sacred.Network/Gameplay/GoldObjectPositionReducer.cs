using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

internal static class GoldObjectPositionReducer
{
    public static bool Apply(Dictionary<uint, GoldReplicatedObject> objects, GoldObjectPositionEvent incoming)
    {
        var correction = incoming.Message.Opcode == GoldCreaturePositionCorrectionCodec.Opcode;
        var entry = correction ? GoldCreaturePositionCorrectionCodec.Decode(incoming.Message) : incoming;
        if (!objects.TryGetValue(entry.ObjectReference, out var actor) || actor.Physical is null ||
            (actor.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0) return false;
        if (entry.Message.Opcode == GoldObjectPositionCodec.Opcode && actor.Creature is null) return false;
        if (correction)
        {
            // Native dereferences the creature cast before its null guard in this case.
            // Reject a non-creature here rather than reproduce the native invalid read.
            if (actor.Creature is not { } creature) return false;
            var physicalBytes = actor.Physical.ToPayload();
            if (physicalBytes.Length >= GoldCreatureStateLayout.PrefixSize &&
                GoldObjectStateReader.HasMarker(physicalBytes, GoldCreatureStateLayout.MarkerOffset, GoldCreatureStateLayout.Marker))
            {
                var flags = BinaryPrimitives.ReadUInt32LittleEndian(physicalBytes.AsSpan(GoldCreatureStateLayout.Word200Offset));
                BinaryPrimitives.WriteUInt32LittleEndian(physicalBytes.AsSpan(GoldCreatureStateLayout.Word200Offset), flags & ~GoldCreatureStateLayout.SleepingFlag);
            }
            actor = actor with { Creature = creature.WithControlWord(GoldCreatureStateLayout.Word200Offset,
                creature.Word200 & ~GoldCreatureStateLayout.SleepingFlag), Physical = GoldPhysicalState.FromPayload(physicalBytes), Sleep = null };
            if ((creature.Word200 & GoldCreaturePositionCorrectionCodec.DeferredPositionFlag) != 0)
            {
                objects[entry.ObjectReference] = actor with { DeferredPosition = new(entry) };
                return true;
            }
            var basis = AtPosition(actor.BaseState, entry.Position);
            var direction = GoldPhysicalFacing.ToDirection(entry.FacingBits);
            byte[] Patch(byte[] bytes)
            {
                basis.ToPayload().CopyTo(bytes, 0);
                return GoldPhysicalState.FromPayload(bytes).WithDirection(direction).ToPayload();
            }
            // A 0x179 does not call the 0x165 action reset. Preserve the current path/action generations.
            objects[entry.ObjectReference] = actor with
            {
                BaseState = basis, Physical = GoldPhysicalState.FromPayload(Patch(actor.Physical.ToPayload())),
                Creature = GoldCreatureState.FromPayload(Patch(actor.Creature!.ToPayload())),
                FacingBits = entry.FacingBits, PositionOpcode = entry.Message.Opcode, PositionVersion = actor.PositionVersion+1
            };
            return true;
        }
        var flightStopped = entry.Message.Opcode == GoldObjectFlightStopCodec.Opcode;
        var relocated = AtPosition(actor.BaseState, entry.Position);
        objects[entry.ObjectReference] = actor with
        {
            BaseState = flightStopped ? relocated with { Flags = relocated.Flags & ~GoldObjectFlightLayout.FlyingFlag } : relocated,
            Flight = flightStopped ? null : actor.Flight, FlightVersion = actor.FlightVersion + (flightStopped ? 1 : 0),
            Physical = actor.Physical.WithDirection(GoldPhysicalFacing.ToDirection(entry.FacingBits)),
            Attack = null, SpecialMove = null, Cast = null, AttackVersion = actor.AttackVersion+1,
            Movement = actor.Creature is not null ? null : actor.Movement, FacingBits = entry.FacingBits,
            PositionOpcode = entry.Message.Opcode, PositionVersion = actor.PositionVersion+1, MovementVersion = actor.MovementVersion+1
        };
        return true;
    }
    private static GoldObjectBaseState AtPosition(GoldObjectBaseState basis, GoldObjectPosition position) => basis with
    {
        X = position.X, Y = position.Y, Surface = unchecked((byte)position.Surface), Sector = unchecked((ushort)position.Sector)
    };
}
