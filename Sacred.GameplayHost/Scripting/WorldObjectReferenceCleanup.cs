namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x5FAB50 queue cleanup. No destruction, reference return or
/// registry shrink occurs here. Invalid native extents remain guarded; callers
/// must discard private state after failure, as with other script primitives.</summary>
public static class WorldObjectReferenceCleanup
{
    public static void Clean<TObject>(uint requested, bool controllerFlag2A,
        IReadOnlyList<TObject?> slots, IList<uint> available,
        WorldObjectReferenceCleanupCallbacks<TObject> callbacks) where TObject : class
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(available);
        ArgumentNullException.ThrowIfNull(callbacks);
        if (controllerFlag2A) return;
        ArgumentNullException.ThrowIfNull(callbacks.ReadReference);
        ArgumentNullException.ThrowIfNull(callbacks.WriteReference);
        ArgumentNullException.ThrowIfNull(callbacks.ReadFlags);
        ArgumentNullException.ThrowIfNull(callbacks.Report);
        if (available.IsReadOnly) throw new NotSupportedException("Reference cleanup requires mutable logical queue storage.");
        var indices = new List<uint>();
        // Gold uses an unsigned loop bound: a doubly decremented negative index
        // exits after increment, rather than starting another logical iteration.
        for (var i = 0; unchecked((uint)i) < (uint)available.Count; i++)
        {
            if (Read(i) == 0) callbacks.Report(new(WorldObjectReferenceCleanupDiagnosticKind.ZeroReference, unchecked((uint)i)));
            var value = WorldActiveHeroLookup.ResolveObject(Read(i), slots,
                callbacks.ReadReference, callbacks.WriteReference,
                (reference, old) => callbacks.Report(new(WorldObjectReferenceCleanupDiagnosticKind.Repair, reference, old)));
            if (value is not null && (callbacks.ReadFlags(value) & 0x10) == 0)
            {
                callbacks.Report(new(WorldObjectReferenceCleanupDiagnosticKind.LiveReference, unchecked((uint)i), Read(i)));
                SwapRemove(i);
                i--;
            }
            // The original reads index -1 after removing the first live entry.
            // Preserve an explicit storage boundary instead of inventing a word.
            var probe = Read(i);
            if (probe != requested) continue;
            indices.Add(unchecked((uint)i));
            if (indices.Count <= 1) continue;
            if (available.Count == 0)
                throw new NotSupportedException("Native reference cleanup would corrupt the logical deque extent; discard private state.");
            SwapRemove(i);
            i--;
        }
        if (indices.Count <= 1) return;
        callbacks.Report(new(WorldObjectReferenceCleanupDiagnosticKind.Duplicates, requested, (uint)indices.Count));
        foreach (var index in indices) callbacks.Report(new(WorldObjectReferenceCleanupDiagnosticKind.DuplicateIndex, index));

        void SwapRemove(int index)
        {
            if (available.Count == 0)
                throw new NotSupportedException("Native reference cleanup would corrupt the logical deque extent; discard private state.");
            if (index >= 0 && index < available.Count) available[index] = available[^1];
            else if (callbacks.WriteOutsideLogical is { } write) write(index, available[^1]);
            else throw new NotSupportedException("Native cleanup requires an established outside-logical storage write.");
            available.RemoveAt(available.Count - 1);
        }
        uint Read(int index)
        {
            if (index >= 0 && index < available.Count) return available[index];
            if (callbacks.ReadOutsideLogical is { } read) return read(index);
            if (index == -1 && callbacks.ReadPredecessor is { } predecessor) return predecessor();
            throw new NotSupportedException("Native cleanup requires established outside-logical storage bytes.");
        }
    }
}
