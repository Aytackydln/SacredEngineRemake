namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x600AA0 selection policy over caller-owned logical registry,
/// available deque and delayed pairs. Storage/synchronization and actual cleanup
/// remain explicit boundaries. Incoming network grant ownership is separate.</summary>
public static class WorldObjectReferenceSelector
{
    public static WorldObjectReferenceSelection Select<TObject>(bool multiplayer,
        IList<TObject?> slots, IList<uint> available, IList<WorldObjectReferenceDelay> delayed,
        WorldObjectReferenceSelectionCallbacks<TObject> callbacks, byte? allocationByte = null) where TObject : class
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(available);
        ArgumentNullException.ThrowIfNull(delayed);
        ArgumentNullException.ThrowIfNull(callbacks);
        ArgumentNullException.ThrowIfNull(callbacks.ReadFlags);
        ArgumentNullException.ThrowIfNull(callbacks.ReadAllocationByte);
        ArgumentNullException.ThrowIfNull(callbacks.InInventory);
        ArgumentNullException.ThrowIfNull(callbacks.InInventoryChest);
        ArgumentNullException.ThrowIfNull(callbacks.Destroy);
        ArgumentNullException.ThrowIfNull(callbacks.Clock);
        ArgumentNullException.ThrowIfNull(callbacks.Cleanup);
        ArgumentNullException.ThrowIfNull(callbacks.ReportRejected);
        if (slots.IsReadOnly || available.IsReadOnly || delayed.IsReadOnly)
            throw new NotSupportedException("Reference selection requires mutable logical session sources.");
        uint selected = 0;
        while (true)
        {
            if (!multiplayer && available.Count < 128)
            {
                while (slots.Count <= 16) slots.Add(null);
                for (var i = 0; i < 256; i++)
                {
                    var reference = checked((uint)slots.Count);
                    slots.Add(null);
                    available.Add(reference);
                    callbacks.Cleanup(reference);
                }
            }
            if (available.Count == 0) break;
            selected = available[0];
            if (selected >= int.MaxValue)
                throw new NotSupportedException("Reference exceeds managed logical registry bounds.");
            while (slots.Count <= (int)selected) slots.Add(null);
            var previous = slots[(int)selected];
            if (previous is not null)
            {
                WorldObjectReferenceRejection? rejection = null;
                if ((callbacks.ReadFlags(previous) & 0x10) == 0) rejection = WorldObjectReferenceRejection.Occupied;
                else if (callbacks.InInventory(selected)) rejection = WorldObjectReferenceRejection.Inventory;
                else if (callbacks.InInventoryChest(selected)) rejection = WorldObjectReferenceRejection.InventoryChest;
                if (rejection is { } reason)
                {
                    callbacks.ReportRejected(selected, reason);
                    available.RemoveAt(0);
                    // Native retains EBX across a retry. If the queue is now
                    // empty it returns/delays this rejected reference unchanged.
                    continue;
                }
                if (allocationByte.HasValue)
                    allocationByte = unchecked((byte)(callbacks.ReadAllocationByte(previous) + 1));
                WorldObjectRegistryRemoval.DestroySlot(selected, slots, callbacks.Destroy);
            }
            // Native pops the current front after destructor reentry, without
            // changing the reference it selected before that callback.
            available.RemoveAt(0);
            break;
        }
        if (selected != 0) delayed.Add(new(selected, callbacks.Clock()));
        callbacks.Cleanup(selected);
        return new(selected, allocationByte);
    }
}
