namespace Sacred.GameplayHost.Networking;

/// <summary>Complete Gold 0x7D33E0 close at explicit service boundaries. Current
/// socket/transport/handles are reread after callbacks. Final logging follows the
/// active-byte clear, so its callback may affect the open caller's raw result.</summary>
public static class WorldConnectionClose
{
    public static void Close(WorldConnectionCloseServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var memory = services.Memory;
        var backend = services.ReadOptionalBackend();
        if (backend != 0)
        {
            services.DestroyOptionalBackend(backend);
            services.WriteOptionalBackend(0);
        }
        services.ClearConnection();
        services.CloseHandle(memory.ReadWord(8));
        services.CloseHandle(memory.ReadWord(0xc));
        services.CloseHandle(memory.ReadWord(0x10));
        if (memory.ReadWord(0x14) != uint.MaxValue) services.ShutdownSocket(memory.ReadWord(0x14));
        if (memory.ReadWord(0x14) != uint.MaxValue) services.CloseSocket(memory.ReadWord(0x14));
        services.CleanupSockets();
        if (memory.ReadWord(0x18) != 0 && memory.ReadWord(0x24) != 0)
        {
            services.InvokeTransport(memory.ReadWord(0x24), 0x3c);
            var current = memory.ReadWord(0x24);
            if (current == 0) throw new NotSupportedException("Native close dereferences a removed current transport.");
            services.InvokeTransport(current, 4);
            memory.WriteWord(0x24, 0);
        }
        for (uint at = 4; at < 0x40414; at += 4) memory.WriteWord(at, 0);
        memory.WriteWord(0x14, uint.MaxValue);
        memory.WriteWord(0x18, 0);
        memory.WriteWord(0x40464, 0);
        memory.WriteWord(0x40460, 0);
        services.ClearFirstQueue();
        services.ClearSecondQueue();
        services.ClearPendingRecords();
        memory.WriteByte(0x2a, 0);
        services.LogClosed();
    }
}
