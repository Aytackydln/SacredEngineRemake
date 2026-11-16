namespace Sacred.GameplayHost.Scripting;

/// <summary>Native usage-query, field, destructor, clock and delayed-cleanup
/// boundaries. Caller serializes registry/pool access for this session.</summary>
public sealed record WorldObjectReferenceSelectionCallbacks<TObject>(
    Func<TObject, uint> ReadFlags, Func<TObject, byte> ReadAllocationByte,
    Func<uint, bool> InInventory, Func<uint, bool> InInventoryChest,
    Action<TObject> Destroy, Func<uint> Clock, Action<uint> Cleanup,
    Action<uint, WorldObjectReferenceRejection> ReportRejected) where TObject : class;

public enum WorldObjectReferenceRejection { Occupied, Inventory, InventoryChest }
