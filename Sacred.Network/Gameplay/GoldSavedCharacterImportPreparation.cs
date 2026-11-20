using System.Collections.Frozen;
using Sacred.Core.Pax.Hero;

namespace Sacred.Network.Gameplay;

/// <summary>Verified physical and inventory import stages for a saved participant.
/// Derived values are retained from the save; recalculation is deferred to gameplay.</summary>
public sealed class GoldSavedCharacterImportPreparation
{
    private GoldSavedCharacterImportPreparation(GoldSavedCharacterReferencePlan references,
        GoldSavedCharacterInventoryPreparation inventories, Dictionary<uint, GoldPhysicalState> physicalStates)
    {
        (References, Inventories, ObjectPhysicalStates) = (references, inventories, physicalStates.ToFrozenDictionary());
        InventoryMembership = GoldSavedInventoryMembership.Create(inventories.Primary.Inventory, inventories.Shared.Inventory);
    }
    public GoldSavedCharacterReferencePlan References { get; }
    public GoldSavedCharacterInventoryPreparation Inventories { get; }
    public IReadOnlyDictionary<uint, GoldPhysicalState> ObjectPhysicalStates { get; }
    public IReadOnlyDictionary<uint, ushort> InventoryMembership { get; }

    /// <summary>Check the save-dependent physical import before contacting GameServer. Reference-dependent
    /// normalization still runs with the actual assigned slot and grants.</summary>
    public static void ValidateObjectPhysicalImports(SacredSavedCharacterRecords source)
    {
        ArgumentNullException.ThrowIfNull(source);
        foreach (var record in source.Objects.Records)
        {
            if (record.IsEmpty) continue;
            try
            {
                if (record.Item is { } item) GoldSavedPhysicalStateImporter.ValidateImport(item.PhysicalData);
                else if (record.Creature is { } creature) GoldSavedPhysicalStateImporter.ValidateImport(creature.PhysicalData);
                else throw new InvalidDataException("Saved object has no supported physical body.");
            }
            catch (NotSupportedException error)
            {
                throw new NotSupportedException($"Saved object {record.BaseState.ObjectReference}, resource {record.BaseState.Resource}: {error.Message}", error);
            }
        }
    }

    public static GoldSavedCharacterImportPreparation Create(GoldSavedCharacterReferencePlan references)
    {
        ArgumentNullException.ThrowIfNull(references);
        var inventories = GoldSavedCharacterInventoryPreparation.Create(references);
        var states = new Dictionary<uint, GoldPhysicalState>();
        foreach (var record in references.Source.Objects.Records)
        {
            if (record.IsEmpty) continue;
            var target = references.References[record.BaseState.ObjectReference];
            try
            {
                if (record.Item is { } item)
                    states.Add(target, GoldSavedPhysicalStateImporter.ImportParticipantObject(item.PhysicalData, target, references.PlayerSlot));
                else if (record.Creature is { } creature)
                    states.Add(target, GoldSavedPhysicalStateImporter.ImportParticipantObject(creature.PhysicalData, target, references.PlayerSlot));
                else throw new InvalidDataException("Saved object has no supported physical body.");
            }
            catch (NotSupportedException error)
            {
                throw new NotSupportedException($"Saved object {record.BaseState.ObjectReference}, resource {record.BaseState.Resource}: {error.Message}", error);
            }
        }
        return new(references, inventories, states);
    }
}
