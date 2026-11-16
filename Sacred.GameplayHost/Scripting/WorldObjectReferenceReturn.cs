namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x5FAF20 queue return and cleanup gate. Reason values retain
/// their raw caller meaning; this does not validate incoming grant ownership.</summary>
public static class WorldObjectReferenceReturn
{
    public static void Return(uint reference, uint reason, bool multiplayer, IList<uint> available,
        Action<uint, uint> reportReturned, Action<uint> cleanup)
    {
        ArgumentNullException.ThrowIfNull(available);
        ArgumentNullException.ThrowIfNull(reportReturned);
        ArgumentNullException.ThrowIfNull(cleanup);
        if (available.IsReadOnly) throw new NotSupportedException("Reference return requires mutable logical queue storage.");
        if (reference != 0)
        {
            if (multiplayer && reason <= 6) reportReturned(reference, reason);
            available.Add(reference);
        }
        // Zero skips enqueue/diagnostics but can still request cleanup.
        if (reason != 3) cleanup(reference);
    }
}
