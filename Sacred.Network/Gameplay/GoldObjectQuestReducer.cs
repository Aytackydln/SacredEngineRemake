using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

internal static class GoldObjectQuestReducer
{
    public static bool Apply(Dictionary<uint, GoldReplicatedObject> objects, GoldObjectQuestEvent entry)
    {
        var update = GoldObjectQuestCodec.Decode(entry.Message);
        if (!objects.TryGetValue(update.ObjectReference, out var actor) || actor.Physical is null ||
            (actor.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0) return false;
        var offset = update.IsEffect ? GoldPhysicalStateLayout.QuestEffectOffset : GoldPhysicalStateLayout.QuestNumberOffset;
        byte[] Patch(byte[] bytes)
        {
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset), update.Value);
            return bytes;
        }
        // Each layer can contain newer independent updates. Patch its current bytes rather
        // than reconstructing subtype state from the retained physical arrival snapshot.
        objects[update.ObjectReference] = actor with
        {
            Physical = GoldPhysicalState.FromPayload(Patch(actor.Physical.ToPayload())),
            Creature = actor.Creature is { } creature ? GoldCreatureState.FromPayload(Patch(creature.ToPayload())) : null,
            Item = actor.Item is { } item ? GoldItemState.FromPayload(Patch(item.ToPayload())) : null,
            Container = actor.Container is { } container ? GoldContainerState.FromPayload(Patch(container.ToPayload())) : null
        };
        // Native receive passes publish=false to both setters: no owner callback.
        return true;
    }
}
