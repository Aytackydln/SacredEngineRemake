using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Compared local family-10 event 0x117 through 0x7D8950's common exit.
/// No wire packet is created. Callers supply an established, stable controller
/// instance and current connection storage across synchronous service callbacks.</summary>
public static class WorldController117
{
    public static void Dispatch(WorldController117Services services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var memory = services.Controller;
        if (memory.ReadByte(0) == 0)
        {
            services.ChatResource("UI_NET_MSG_NOCONNECTION");
            return;
        }
        if (memory.ReadByte(8) != 0) return;
        var connection = RequireConnection(services);
        if (connection.Memory.ReadByte(0x2b) != 0)
        {
            WorldConnectionClear.Clear(connection);
            if (memory.ReadByte(0x1c) != 0) WorldHostedServerProcessStop.Stop(memory, services.HostedProcess);
            var flag3819 = memory.ReadByte(0x3819);
            memory.WriteByte(0x1c, 0);
            memory.WriteWord(0x24, 0);
            memory.WriteByte(2, 0);
            memory.WriteByte(0x28, 0);
            memory.WriteByte(1, 0);
            memory.WriteByte(9, 0);
            if (flag3819 != 0 && memory.ReadByte(0x381b) == 0) memory.WriteByte(0x49, 0);
            services.ClearPendingVector();
            connection = RequireConnection(services);
        }
        if ((connection.Memory.ReadWord(0x40424) & 0x80) != 0 || memory.ReadByte(0x28) != 0) return;
        if (services.QueryBackend())
        {
            if (memory.ReadByte(0x381b) != 0)
            {
                connection = services.ReadConnection();
                if (connection is not null) services.BackendCommand(connection, 5);
            }
            else services.Ui(new(0x36, "UI_WND_NETWORK", 4, 0) { NativeWord0C = 2 });
        }
        else services.Ui(new(0x36, "UI_WND_NETWORK", 1, 1) { NativeWord0C = 0 });
    }

    private static WorldConnectionClearServices RequireConnection(WorldController117Services services) =>
        services.ReadConnection() ?? throw new NotSupportedException("Native local 0x117 dereferences a missing connection before its later guard.");

    public static WorldController117Snapshot Capture(WorldController117Services services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var memory = services.Controller;
        var connection = services.ReadConnection();
        return new(new(memory.ReadWord(0x14), memory.ReadWord(0x24), connection?.Memory.ReadWord(0x48)),
            new(memory.ReadByte(0), memory.ReadByte(2), memory.ReadByte(9), memory.ReadByte(0x28)),
            new(memory.ReadByte(0x3818), memory.ReadByte(0x3819), memory.ReadByte(0x381a),
                memory.ReadByte(0x381b), memory.ReadByte(0x381c), memory.ReadByte(0x381d)),
            memory.ReadByte(8), memory.ReadByte(0x1c), memory.ReadWord(0x20), memory.ReadByte(0x49))
        { HostByte1 = memory.ReadByte(1), PendingByte3 = memory.ReadByte(3) };
    }
}
