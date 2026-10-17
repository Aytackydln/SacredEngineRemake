using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>Produces participant character packets from original saves without blocking on derived-value calculations.</summary>
public static class GoldSavedCharacterPacketPreparation
{
    public static GoldParticipantCharacterUpload Create(GoldSavedCharacterImportPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        var plan = preparation.References;
        var states = new List<byte[]>(plan.ObjectCount);
        foreach (var record in plan.Source.Objects.Records)
        {
            if (record.IsEmpty) continue;
            var physical = preparation.ObjectPhysicalStates[plan.References[record.BaseState.ObjectReference]];
            if (record.Item is { } item)
            {
                var records = new byte[item.Count * GoldItemStateLayout.ItemDataSize];
                for (var i = 0; i < item.Count; i++) item.GetRecord(i).CopyTo(records.AsSpan(i * GoldItemStateLayout.ItemDataSize));
                // TODO: Recalculate item price/derived values when needed by gameplay. Stored values suffice for upload.
                // Runtime +0x1E0 starts at zero, then native 0x5DD8C0 assigns visible-grid inventory membership.
                // The saved trailer is not restored into it by the native loader.
                preparation.InventoryMembership.TryGetValue(physical.BaseState.ObjectReference, out var inventoryId);
                states.Add(GoldItemState.Create(physical, records, item.ItemData, inventoryId, item.WeaponExtension).ToPayload());
            }
            else if (record.Creature is { } creature)
                states.Add(GoldSavedCreatureStateImporter.Import(creature, physical, plan.References, 0x16E).ToPayload());
            else throw new InvalidDataException("Unsupported saved participant object.");
        }
        var hero = plan.Source.Hero.Creature;
        var heroPhysical = GoldSavedPhysicalStateImporter.ImportRetainedState(hero.PhysicalData);
        var heroBytes = heroPhysical.ToPayload();
        var basis = heroPhysical.BaseState with
        {
            ObjectReference = plan.PlayerSlot,
            Owner = plan.PlayerSlot,
            Flags = (heroPhysical.BaseState.Flags & ~0x200010u) | 0x200u
        };
        basis.ToPayload().CopyTo(heroBytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(heroBytes.AsSpan(GoldPhysicalStateLayout.WordB0Offset), heroPhysical.WordB0 & ~0x2EF07FFFu);
        var heroState = GoldSavedCreatureStateImporter.Import(hero, GoldPhysicalState.FromPayload(heroBytes), plan.References, 0x169);
        return GoldParticipantCharacterUpload.Create(plan, states, heroState.ToPayload(),
            preparation.Inventories.Primary.Inventory, preparation.Inventories.Shared.Inventory);
    }
}
