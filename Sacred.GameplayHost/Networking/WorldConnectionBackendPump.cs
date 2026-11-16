namespace Sacred.GameplayHost.Networking;

/// <summary>Compared 0x7D5B60 pending-pointer transfer and 0x7D5F80 pump/query.
/// This does not dispatch packets or replace the existing connection reader/queue.
/// Callbacks are synchronous native service boundaries; partial failure must not
/// be acknowledged or automatically replayed by an admission consumer.</summary>
public static class WorldConnectionBackendPump
{
    public static void Pump<TBackend>(WorldConnectionBackendPumpServices<TBackend> services) where TBackend : class
    {
        ArgumentNullException.ThrowIfNull(services);
        var backend = services.ReadBackend();
        if (backend is null) return;
        services.Poll(backend);
        backend = RequireBackend(services);
        services.EnterBackend(backend);
        backend = RequireBackend(services);
        var count = services.PendingCount(backend);
        if (count > 0)
        {
            for (var index = 0; index < count; index++)
            {
                var value = services.ReadPending(backend, index);
                if (value == 0) continue;
                services.EnterConnection();
                services.AppendConnection(value);
                services.ExitConnection();
                backend = RequireBackend(services);
            }
            // Native captures the iteration extent, then clears the current
            // backend's entire extent, including appends made during transfer.
            if (services.PendingCount(backend) != 0) services.ClearPending(backend);
        }
        services.ExitBackend(backend);
    }

    public static bool PumpAndQuery<TBackend>(WorldConnectionBackendPumpServices<TBackend> services) where TBackend : class
    {
        Pump(services);
        // Pump and its callbacks may replace/remove the backend. Query its
        // current identity, not the one captured before polling or locking.
        return services.ReadBackend() is { } backend && services.Query(backend);
    }

    /// <summary>0x7D5F50 pumps first and forwards its command to the current
    /// backend via 0x7CEAD0. The local 0x117 caller ignores the boolean result.</summary>
    public static bool PumpAndCommand<TBackend>(WorldConnectionBackendPumpServices<TBackend> services,
        uint command, Func<TBackend, uint, bool> dispatch) where TBackend : class
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        Pump(services);
        return services.ReadBackend() is { } backend && dispatch(backend, command);
    }

    private static TBackend RequireBackend<TBackend>(WorldConnectionBackendPumpServices<TBackend> services) where TBackend : class =>
        services.ReadBackend() ?? throw new NotSupportedException(
            "Native pump dereferences a missing backend during transfer; its lifetime must remain established through unlock.");
}
