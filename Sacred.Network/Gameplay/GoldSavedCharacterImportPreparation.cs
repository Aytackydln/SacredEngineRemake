using System.Collections.Frozen;

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

    public static GoldSavedCharacterImportPreparation Create(GoldSavedCharacterReferencePlan references)
    {
        ArgumentNullException.ThrowIfNull(references);
        var inventories = GoldSavedCharacterInventoryPreparation.Create(references);
        var states = new Dictionary<uint, GoldPhysicalState>();
        foreach (var record in references.Source.Objects.Records)
        {
            if (record.IsEmpty) continue;
            var target = references.References[record.BaseState.ObjectReference];
            GoldPhysicalState state;
            if (record.Item is { } item)
                state = GoldSavedPhysicalStateImporter.ImportParticipantObject(item.PhysicalData, target, references.PlayerSlot);
            else if (record.Creature is { } creature)
                state = GoldSavedPhysicalStateImporter.ImportParticipantObject(creature.PhysicalData, target, references.PlayerSlot);
            else throw new InvalidDataException("Saved object has no supported physical body.");
            states.Add(target, state);
        }
        return new(references, inventories, states);
    }
}
