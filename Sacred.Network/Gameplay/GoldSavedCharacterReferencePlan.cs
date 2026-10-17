using System.Buffers.Binary;
using System.Collections.ObjectModel;
using Sacred.Core.Pax.Hero;
using Sacred.Core.Pax.Hero.Inventory;
using Sacred.Core.Pax.Hero.Objects;

namespace Sacred.Network.Gameplay;

/// <summary>Stage one-pass saved-to-session references using this participant's grants. No upload or actor creation.</summary>
public sealed class GoldSavedCharacterReferencePlan
{
    private GoldSavedCharacterReferencePlan(SacredSavedCharacterRecords source, byte slot, Dictionary<uint, uint> references, SacredSavedInventory inventory, SacredSavedInventory? shared)
        => (Source, PlayerSlot, References, Inventory, SharedInventory) = (source, slot, new ReadOnlyDictionary<uint, uint>(references), inventory, shared);
    public SacredSavedCharacterRecords Source { get; }
    public byte PlayerSlot { get; }
    public IReadOnlyDictionary<uint, uint> References { get; }
    public int ObjectCount => References.Count - 1;
    public SacredSavedInventory Inventory { get; }
    public SacredSavedInventory? SharedInventory { get; }
    public static GoldSavedCharacterReferencePlan Create(SacredSavedCharacterRecords source, byte playerSlot, ReadOnlySpan<uint> allocatedReferences)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (playerSlot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(playerSlot));
        if (!source.Hero.RemainingData.IsEmpty || !source.Objects.RemainingData.IsEmpty || !source.Inventory.RemainingData.IsEmpty ||
            source.SharedInventory is { RemainingData.IsEmpty: false }) throw new InvalidDataException("Reference preparation requires fully accounted source records.");
        var map = new Dictionary<uint, uint>();
        var granted = new HashSet<uint>();
        var hero = source.Hero.Creature;
        if (hero.BaseState.ObjectReference == 0) throw new InvalidDataException("Saved hero has no reference.");
        map.Add(hero.BaseState.ObjectReference, playerSlot);
        var cursor = 0;
        foreach (var record in source.Objects.Records)
        {
            if (record.IsEmpty) continue;
            var original = record.BaseState.ObjectReference;
            if (original == 0 || map.ContainsKey(original)) throw new InvalidDataException("Saved object references are zero or ambiguous.");
            if (cursor == allocatedReferences.Length) throw new InvalidDataException("Not enough allocated object references for this saved character.");
            var target = allocatedReferences[cursor++];
            if (target is < GoldReferenceCodec.FirstObjectReference or >= GoldReferenceCodec.ReferenceLimit || !granted.Add(target))
                throw new InvalidDataException("Object grant contains an invalid or duplicate reference.");
            map.Add(original, target);
        }
        for (var slot = 0; slot < SacredSavedCreatureLayout.EquipmentCount; slot++) Resolve(hero.GetEquipmentReference(slot), map);
        Resolve(hero.MountPartnerReference, map);
        foreach (var record in source.Objects.Records)
            if (record.Creature is { } creature)
            {
                for (var slot = 0; slot < SacredSavedCreatureLayout.EquipmentCount; slot++) Resolve(creature.GetEquipmentReference(slot), map);
                Resolve(creature.MountPartnerReference, map);
            }
        var inventory = RewriteInventory(source.Inventory, map, playerSlot);
        var shared = source.SharedInventory is { } bank ? RewriteInventory(bank, map, 17) : null;
        return new(source, playerSlot, map, inventory, shared);
    }

    private static uint Resolve(uint reference, Dictionary<uint, uint> map)
        => reference == 0 ? 0 : map.TryGetValue(reference, out var target) ? target : throw new InvalidDataException("Saved creature/inventory refers to an unimported object.");

    private static SacredSavedInventory RewriteInventory(SacredSavedInventory source, Dictionary<uint, uint> map, ushort id)
    {
        var payload = source.ToPayload();
        BinaryPrimitives.WriteUInt16LittleEndian(payload, id);
        for (var cell = 0; cell < SacredSavedInventoryLayout.CellCount; cell++)
        {
            var offset = SacredSavedInventoryLayout.CellsOffset + cell * SacredSavedInventoryLayout.CellSize;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset), Resolve(source.GetCell(cell).ObjectReference, map));
        }
        for (var index = 0; index < SacredSavedInventoryLayout.ItemBarCount; index++)
        {
            var offset = SacredSavedInventoryLayout.ItemBarOffset + index * SacredSavedInventoryLayout.ItemBarRecordSize;
            var reference = BinaryPrimitives.ReadUInt32LittleEndian(source.GetItemBarRecord(index));
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset), Resolve(reference, map));
            // Native 0x5D8120 invalidates both cached words whenever a nonzero item reference is replaced.
            if (reference != 0) payload.AsSpan(offset + 4, 8).Clear();
        }
        return SacredSavedInventory.FromPayload(payload);
    }
}
