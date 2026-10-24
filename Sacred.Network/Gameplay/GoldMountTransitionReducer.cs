using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

internal static class GoldMountTransitionReducer
{
    public static bool Apply(GoldMountTransitionEvent entry, Dictionary<uint, GoldReplicatedObject> objects)
    {
        // The receiver checks the initiating creature. The downstream partner lookup checks RTTI,
        // but deliberately has no inactive guard. Preserve that difference.
        if (!objects.TryGetValue(entry.PartnerReference, out var partner) || partner.Creature is null) return false;
        void Change(uint reference, uint add, uint clear)
        {
            var actor = objects[reference]; var state = actor.Creature!;
            state = state.WithControlWord(GoldCreatureStateLayout.Word200Offset, (state.Word200 | add) & ~clear);
            if (!entry.Mounts)
            {
                var bytes = state.ToPayload();
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.Word2B7Offset), (ushort)(state.Word2B7 & ~0x10));
                state = GoldCreatureState.FromPayload(bytes);
            }
            objects[reference] = actor with { Creature = state };
        }
        Change(entry.PartnerReference, entry.Mounts ? 8u : 0u, entry.Mounts ? 0x10u : 0x18u);
        Change(entry.ObjectReference, entry.Mounts ? 4u : 0u, entry.Mounts ? 0x10u : 0x14u);
        return true;
    }
}
