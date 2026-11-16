namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x5FB360 cancellation and timed delayed-reference scan. Actual
/// registry slots, available queue and records are supplied by the same session.</summary>
public static class WorldObjectReferenceMaintenance
{
    public static void Maintain<TObject>(uint suppliedReference, bool multiplayer,
        IReadOnlyList<TObject?> slots, IList<uint> available, WorldObjectReferenceMaintenanceState state,
        WorldObjectReferenceMaintenanceCallbacks<TObject> callbacks) where TObject : class
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(available);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(callbacks);
        ArgumentNullException.ThrowIfNull(callbacks.Clock);
        ArgumentNullException.ThrowIfNull(callbacks.ReadObjectType);
        ArgumentNullException.ThrowIfNull(callbacks.Cleanup);
        ArgumentNullException.ThrowIfNull(callbacks.ReportOccupied);
        ArgumentNullException.ThrowIfNull(callbacks.ReportReturned);
        if (available.IsReadOnly || state.Records.IsReadOnly)
            throw new NotSupportedException("Reference maintenance requires mutable logical session sources.");
        if (suppliedReference != 0)
            for (var i = 0; i < state.Records.Count; i++)
                if (state.Records[i].Reference == suppliedReference)
                {
                    SwapRemove(i);
                    break;
                }
        var now = callbacks.Clock();
        if (unchecked(now - state.LastSweep) <= 2000) return;
        state.LastSweep = now;
        for (var i = 0; i < state.Records.Count;)
        {
            var record = state.Records[i];
            if (unchecked(now - record.Time) <= 60000) { i++; continue; }
            if (record.Reference < (uint)slots.Count && slots[(int)record.Reference] is { } occupied)
                callbacks.ReportOccupied(record.Reference, callbacks.ReadObjectType(occupied));
            else
            {
                if (record.Reference != 0)
                {
                    if (multiplayer) callbacks.ReportReturned(record.Reference);
                    available.Add(record.Reference);
                }
                callbacks.Cleanup(record.Reference);
            }
            // Native reloads the record table after diagnostics/cleanup; swap
            // removal then reexamines the moved last row at this same index.
            SwapRemove(i);
        }

        void SwapRemove(int index)
        {
            if ((uint)index >= (uint)state.Records.Count)
                throw new NotSupportedException("Reference maintenance callback invalidated the delayed extent; discard private state.");
            state.Records[index] = state.Records[^1];
            state.Records.RemoveAt(state.Records.Count - 1);
        }
    }
}
