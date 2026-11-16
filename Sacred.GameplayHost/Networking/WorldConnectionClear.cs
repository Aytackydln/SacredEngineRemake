namespace Sacred.GameplayHost.Networking;

/// <summary>Compared 0x7D4090 connection clear. Captured instance storage is
/// retained, including its backend pointer. Node allocation and OS/backend services
/// are caller-owned; partial failures must not be silently replayed.</summary>
public static class WorldConnectionClear
{
    public static void Clear(WorldConnectionClearServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var memory = services.Memory;
        var session = memory.ReadWord(0x1c);
        if (session != 0 && memory.ReadWord(0x24) != 0)
        {
            services.ReleaseBackendSession(session);
            memory.WriteWord(0x1c, 0);
        }
        if (memory.ReadByte(0x2b) != 0) services.LogClosed();
        memory.WriteByte(0x2b, 0);
        memory.WriteByte(0x2c, 0);
        foreach (var at in IdentityWords) memory.WriteWord(at, 0);
        memory.WriteHalfWord(0x40058, 0);
        memory.WriteHalfWord(0x4005a, 0);
        foreach (var at in ControlWords) memory.WriteWord(at, 0);
        services.ClearRetainedText();
        memory.WriteWord(0x40444, 0);
        memory.WriteWord(0x40448, 0);
        ClearQueue(services.FirstQueue);
        ClearQueue(services.SecondQueue);
    }

    private static void ClearQueue(WorldConnectionQueueClearServices queue)
    {
        queue.Enter();
        var head = queue.ReadHead();
        var node = queue.ReadNext(head);
        uint count = 0;
        while (node != head)
        {
            node = queue.ReadNext(node);
            count = unchecked(count + 1);
        }
        if (count != 0)
        {
            head = queue.ReadHead();
            node = queue.ReadNext(head);
            if (node != head)
            {
                do
                {
                    var previous = node;
                    node = queue.ReadNext(node);
                    if (previous != 0) queue.FreeNode(previous);
                } while (node != queue.ReadHead());
            }
            head = queue.ReadHead();
            queue.WriteNext(head, head);
            queue.WritePrevious(head, head);
        }
        queue.Exit();
    }

    /// <summary>0x48 is the connection-local reference; the other reset words
    /// retain their native offsets until their wider semantics are established.</summary>
    private static ReadOnlySpan<uint> IdentityWords => [0x30, 0x34, 0x38, 0x3c, 0x40, 0x44, 0x48];
    /// <summary>Includes 0x40424 connection flags; backing/capacity are retained.</summary>
    private static ReadOnlySpan<uint> ControlWords =>
        [0x40054, 0x40418, 0x4041c, 0x40420, 0x40424, 0x40428, 0x4042c, 0x40430, 0x40434];
}
