namespace Sacred.GameplayHost.Networking;

/// <summary>Gold 0x7F1840. Caller owns controller-word writes, pending connection
/// records and record teardown/pump services. Query success is not actor readiness.</summary>
public static class WorldAdmissionFinalizer
{
    public static bool Reset<TRecord>(bool connectionPresent, IList<TRecord> pending,
        Action clearControllerWords30And34, Action<TRecord> destroyRecord, Func<bool> pumpAndQuery)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(clearControllerWords30And34);
        ArgumentNullException.ThrowIfNull(destroyRecord);
        ArgumentNullException.ThrowIfNull(pumpAndQuery);
        if (connectionPresent && pending.IsReadOnly)
            throw new NotSupportedException("Admission reset requires mutable pending-record storage.");
        clearControllerWords30And34();
        if (!connectionPresent) return false;
        // The native routine captures the original pending range. Record
        // destructors are service boundaries and must not replace that range.
        var count = pending.Count;
        for (var i = 0; i < count; i++) destroyRecord(pending[i]);
        pending.Clear();
        return pumpAndQuery();
    }
}
