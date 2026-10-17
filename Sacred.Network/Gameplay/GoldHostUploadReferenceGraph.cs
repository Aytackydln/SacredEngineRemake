using System.Buffers.Binary;
using Sacred.Core.Pax.Hero.Inventory;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Known object-instance links only. Uninterpreted bank keys, container vector words
/// and resource IDs are never treated as object references.</summary>
internal static class GoldHostUploadReferenceGraph
{
    public static (int ObjectCount, ushort HeroSector) Validate(byte slot, IReadOnlyList<GoldMessage> messages,
        IReadOnlyCollection<uint> allocatedReferences)
    {
        var allocated = allocatedReferences.ToHashSet();
        if (allocated.Count != allocatedReferences.Count || allocated.Any(reference => reference < GoldReferenceCodec.FirstObjectReference ||
            reference >= GoldReferenceCodec.ReferenceLimit))
            throw new InvalidDataException("Invalid or duplicate allocated host object references.");
        var events = messages.Select(GoldGameEventDecoder.Decode).ToArray();
        var objects = new Dictionary<uint, GoldObjectEvent>();
        foreach (var entry in events)
        {
            switch (entry)
            {
                case GoldObjectEvent obj when obj.Message.Opcode is 0x169 or 0x16E or 0x16F:
                    var basis = obj.BaseState;
                    if (obj.ObjectReference != basis.ObjectReference || obj.ObjectType != basis.ObjectType || basis.Owner != slot ||
                        basis.ObjectReference != slot && !allocated.Contains(basis.ObjectReference))
                        throw new InvalidDataException("Host object identity, owner or reservation differs from its prepared state.");
                    if (objects.TryGetValue(basis.ObjectReference, out var previous) && previous.ObjectType != obj.ObjectType)
                        throw new InvalidDataException("Host upload changes an existing object's resource identity.");
                    if (obj.Position is { } position && position != new GoldObjectPosition(basis.X, basis.Y, basis.Surface, basis.Sector))
                        throw new InvalidDataException("Host object's outer position differs from its serialized base state.");
                    if (obj.CreatureState is not null && (obj.CreatureTail is null || !obj.CreatureTail.RemainingData.IsEmpty))
                        throw new InvalidDataException("Host creature requires complete values and trailer.");
                    objects[basis.ObjectReference] = obj;
                    break;
                case GoldInventoryEvent:
                case GoldWorldBankEvent:
                case GoldSectorInterestEvent:
                case GoldCreatureUpdateEvent:
                case GoldCreatureValuesEvent:
                case GoldCreatureMountEvent:
                case GoldEquipmentRemovalEvent:
                case GoldObjectTriggerEvent:
                case GoldDeathScriptEvent:
                case GoldWorldRegionFlagsEvent:
                    break;
                default:
                    throw new InvalidDataException($"Unresolved host upload operation 0x{entry.Message.Opcode:X4}.");
            }
        }
        if (!objects.TryGetValue(slot, out var hero) || hero.Message.Opcode != 0x169 || hero.CreatureState is null ||
            events.OfType<GoldObjectEvent>().Count(obj => obj.Message.Opcode == 0x169) != 1)
            throw new InvalidDataException("Host upload requires exactly one complete owned hero.");
        if (allocated.Any(reference => !objects.ContainsKey(reference)))
            throw new InvalidDataException("An allocated host object has no serialized state.");
        foreach (var entry in events)
        {
            switch (entry)
            {
                case GoldObjectEvent obj when obj.CreatureState is { } creature:
                    foreach (var reference in creature.EquipmentReferences) RequireObject(reference);
                    RequireObject(obj.CreatureTail!.MountPartnerReference);
                    break;
                case GoldInventoryEvent inventory:
                    if (inventory.InventoryId != inventory.Inventory.InventoryId || !inventory.Inventory.RemainingData.IsEmpty)
                        throw new InvalidDataException("Host inventory outer identity or parsed extent is inconsistent.");
                    for (var i = 0; i < SacredSavedInventoryLayout.CellCount; i++) RequireObject(inventory.Inventory.GetCell(i).ObjectReference);
                    for (var i = 0; i < SacredSavedInventoryLayout.ItemBarCount; i++)
                        RequireObject(BinaryPrimitives.ReadUInt32LittleEndian(inventory.Inventory.GetItemBarRecord(i)));
                    break;
                case GoldCreatureUpdateEvent update: RequireCreature(update.ObjectReference, update.ObjectType); break;
                case GoldCreatureValuesEvent values: RequireCreature(values.ObjectReference, values.ObjectType); break;
                case GoldEquipmentRemovalEvent removal:
                    RequireCreature(removal.CreatureReference); RequireObject(removal.ExpectedItemReference); break;
                case GoldCreatureMountEvent mount: RequireCreature(mount.FirstReference); RequireCreature(mount.SecondReference); break;
                case GoldObjectTriggerEvent trigger: RequireObject(trigger.ObjectReference); break;
                case GoldDeathScriptEvent script: RequireObject(script.Registration.ObjectReference); break;
                case GoldWorldBankEvent bank when bank.Snapshot.Opcode == GoldWorldBankLayout.Bank2Opcode:
                    foreach (var callback in GoldObjectScriptCallbackCodec.Read(bank.Snapshot)) RequireObject(callback.ObjectReference);
                    break;
            }
        }
        return (objects.Count, hero.BaseState.Sector);

        void RequireObject(uint reference)
        {
            if (reference != 0 && !objects.ContainsKey(reference))
                throw new InvalidDataException($"Host upload contains unresolved object reference {reference}.");
        }
        void RequireCreature(uint reference, uint? resource = null)
        {
            RequireObject(reference);
            if (reference != 0 && (objects[reference].CreatureState is null || resource is { } type && objects[reference].ObjectType != type))
                throw new InvalidDataException($"Host operation requires creature {reference} with its matching resource.");
        }
    }
}
