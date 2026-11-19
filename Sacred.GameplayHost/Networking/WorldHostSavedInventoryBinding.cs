using Sacred.GameplayHost.Equipment;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Original inventory fix/membership against actual factory objects.
/// Reuses existing native-compared remapping/fix services; source-derived packet
/// projections are not accepted as runtime object existence. No upload occurs.</summary>
public sealed record WorldHostSavedInventoryBinding(GoldInventoryImportResult Primary, GoldInventoryImportResult Shared,
    IReadOnlyDictionary<uint, ushort> Membership)
{
    internal static WorldHostSavedInventoryBinding Prepare(GoldSavedCharacterReferencePlan plan,
        WorldCreatureInitialActor actor, IReadOnlyDictionary<uint, WorldEquipmentInitialItem> items)
    {
        if (!actor.SavedEquipmentReferencesBound || !actor.Node.Active || items.Count != plan.ObjectCount)
            throw new InvalidOperationException("Inventory binding requires the current actual saved-object graph.");
        return Fix(plan.Inventory, plan.SharedInventory ?? throw new InvalidDataException("Original shared inventory absent."), actor, items);
    }

    internal WorldHostSavedInventoryBinding Refix(WorldCreatureInitialActor actor, IReadOnlyDictionary<uint, WorldEquipmentInitialItem> items)
        => Fix(Primary.Inventory, Shared.Inventory, actor, items);

    internal WorldHostSavedInventoryBinding ClearPrimaryQuests() => this with
    {
        Primary = Primary with { Inventory = GoldSavedInventoryQuestReset.Apply(Primary.Inventory) }
    };

    private static WorldHostSavedInventoryBinding Fix(Sacred.Core.Pax.Hero.Inventory.SacredSavedInventory primarySource,
        Sacred.Core.Pax.Hero.Inventory.SacredSavedInventory sharedSource, WorldCreatureInitialActor actor,
        IReadOnlyDictionary<uint, WorldEquipmentInitialItem> items)
    {
        // A transient resource projection for the existing fix service, derived
        // solely from the real factory; it is not another object registry.
        var resources = items.ToDictionary(pair => pair.Key, pair => pair.Value.Physical.Base.ObjectType);
        resources.Add(actor.Base.ObjectReference, actor.Base.ObjectType);
        var primary = GoldSavedInventoryImporter.Import(primarySource, resources);
        var shared = GoldSavedInventoryImporter.Import(sharedSource, resources);
        var membership = GoldSavedInventoryMembership.Create(primary.Inventory, shared.Inventory);
        foreach (var reference in primary.ClearRemovalFlagReferences.Concat(shared.ClearRemovalFlagReferences).Concat(membership.Keys))
            if (!items.TryGetValue(reference, out var item) || !item.Node.Active || !item.SavedPostLoadCompleted)
                throw new NotSupportedException("Inventory requires an actual active normalized item; other inventory object effects remain guarded.");
        return new(primary, shared, membership);
    }

    internal void Commit(IReadOnlyDictionary<uint, WorldEquipmentInitialItem> items)
    {
        foreach (var reference in Primary.ClearRemovalFlagReferences.Concat(Shared.ClearRemovalFlagReferences))
            items[reference].Physical.Base = items[reference].Physical.Base with { Flags = items[reference].Physical.Base.Flags & ~GoldSavedInventoryImporter.ObjectRemovalFlag };
        foreach (var (reference, inventory) in Membership) items[reference].InventoryId = inventory;
    }
}
