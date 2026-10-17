namespace Sacred.Network.Gameplay;

/// <summary>Apply the supported native inventory fix after owned-reference replacement, without changing source files or object packets.</summary>
public sealed record GoldSavedCharacterInventoryPreparation(GoldInventoryImportResult Primary, GoldInventoryImportResult Shared)
{
    public static GoldSavedCharacterInventoryPreparation Create(GoldSavedCharacterReferencePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var shared = plan.SharedInventory ?? throw new InvalidDataException("Saved inventory preparation requires a shared inventory stream.");
        var resources = new Dictionary<uint, uint> { [plan.PlayerSlot] = plan.Source.Hero.Creature.BaseState.Resource };
        foreach (var record in plan.Source.Objects.Records)
            if (!record.IsEmpty) resources.Add(plan.References[record.BaseState.ObjectReference], record.BaseState.Resource);
        return new(GoldSavedInventoryImporter.Import(plan.Inventory, resources), GoldSavedInventoryImporter.Import(shared, resources));
    }
}
