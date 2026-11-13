using Sacred.Core.Pak.Items;
using Sacred.GameplayHost.Equipment;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Native 0x4852AF..0x48563C publication requests, retained privately
/// until complete campaign upload. Actor first, then equipment in slot order;
/// allocation/authored order is different. No transport is performed here.</summary>
public static class WorldCreatureInitialPublication
{
    public static IReadOnlyList<GoldMessage> Prepare(WorldCreatureInitialActor actor, GoldCreatureState creature,
        IReadOnlyDictionary<uint, WorldEquipmentInitialItem> registry, Func<uint, ItemsPakEntry> descriptor)
    {
        var messages = new List<GoldMessage> { GoldObjectStateCodec.Create(0x16f, actor.Base.Owner, creature.ToPayload()) };
        var states = new List<(WorldEquipmentInitialItem Item, GoldItemState State)>();
        for (var slot = 0; slot < 18; slot++)
        {
            var reference = actor.EquipmentReferences[slot];
            if (reference == 0) continue;
            if (!registry.TryGetValue(reference, out var item))
                throw new InvalidDataException("Initial publication requires the complete installed item registry.");
            var state = WorldEquipmentInitialSerialization.Write(item, descriptor(item.Physical.Base.ObjectType));
            messages.Add(GoldObjectStateCodec.Create(0x16e, actor.Base.Owner, state.ToPayload()));
            states.Add((item, state));
        }
        foreach (var (item, state) in states) item.SerializedState = state;
        return messages.AsReadOnly();
    }
}
