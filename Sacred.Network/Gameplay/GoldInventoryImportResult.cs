using Sacred.Core.Pax.Hero.Inventory;

namespace Sacred.Network.Gameplay;

/// <summary>Inventory fix output and the referenced objects whose flag 0x10 must be cleared.</summary>
public sealed record GoldInventoryImportResult(SacredSavedInventory Inventory,
    IReadOnlySet<uint> ClearRemovalFlagReferences, int ChangedCachedTypes);
