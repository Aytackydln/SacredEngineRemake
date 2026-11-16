namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x5FAFE0 explicit-reference insertion. Storage is caller-owned
/// logical slots, including holes/entry zero. Reference selection, class/resource
/// resolution and higher caller cleanup are separate. Pending-free cleanup
/// removes only the first matching reference after marked-object destruction.</summary>
public static class WorldObjectRegistryInsertion
{
    public static TObject? AllocateExplicit<TObject>(uint reference, IList<TObject?> slots,
        IList<uint> pendingFreeReferences, WorldObjectAllocationCounter counter,
        WorldObjectRegistryInsertionCallbacks<TObject> callbacks) where TObject : class
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(pendingFreeReferences);
        ArgumentNullException.ThrowIfNull(counter);
        ArgumentNullException.ThrowIfNull(callbacks);
        ArgumentNullException.ThrowIfNull(callbacks.Construct);
        ArgumentNullException.ThrowIfNull(callbacks.ReadFlags);
        ArgumentNullException.ThrowIfNull(callbacks.Destroy);
        ArgumentNullException.ThrowIfNull(callbacks.Prepare);
        ArgumentNullException.ThrowIfNull(callbacks.Activate);
        if (reference == 0 || reference >= int.MaxValue)
            throw new NotSupportedException("Explicit registry insertion requires a nonzero managed logical slot index.");
        if (pendingFreeReferences.IsReadOnly && pendingFreeReferences.Count != 0)
            throw new NotSupportedException("Registry insertion requires mutable pending-free references.");

        counter.BeginAllocation();
        while (slots.Count <= (int)reference) slots.Add(null);
        var created = callbacks.Construct(reference);
        if (created is null) return null;
        // Read after construction: it may have changed the existing slot.
        var previous = slots[(int)reference];
        if (previous is not null && (callbacks.ReadFlags(previous) & 0x10) != 0)
        {
            WorldObjectRegistryRemoval.DestroySlot(reference, slots, callbacks.Destroy);
            // Native traverses after destructor reentry, erasing only the first
            // match in place. Constructor failure and unmarked overwrites skip it.
            for (var i = 0; i < pendingFreeReferences.Count; i++)
                if (pendingFreeReferences[i] == reference)
                {
                    pendingFreeReferences.RemoveAt(i);
                    break;
                }
        }
        // A live occupied slot is overwritten without destruction in this
        // low-level path; its higher caller cleanup must be established separately.
        callbacks.Prepare(created, counter.Value);
        slots[(int)reference] = created;
        callbacks.Activate(created);
        return created;
    }
}
