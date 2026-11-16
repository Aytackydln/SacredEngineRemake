namespace Sacred.GameplayHost.Networking;

/// <summary>Gold complete 0x7D2BD0 control flow at explicit platform/SDK/storage
/// boundaries. Active transport, optional backend and raw return byte differ.</summary>
public static class WorldConnectionOpen
{
    /// <summary>Complete 0x7CF010 query: the optional backend's +0x0C pointer
    /// establishes this result, independently of active transport byte +0x2A.</summary>
    public static bool HasOptionalConnection(WorldControlMemory backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        return backend.ReadWord(0xc) != 0;
    }

    public static byte Open(WorldConnectionOpenServices services, uint parameter, uint port, byte mode)
    {
        ArgumentNullException.ThrowIfNull(services);
        var memory = services.Memory;
        if (memory.ReadByte(0x2a) != 0) services.Close();
        for (uint at = 4; at < 0x40414; at += 4) memory.WriteWord(at, 0);
        memory.WriteWord(0x14, uint.MaxValue);
        services.ClearPendingRecords();
        memory.WriteWord(4, parameter);
        services.ClearFirstQueue();
        services.ClearSecondQueue();
        var startup = services.Sockets.Startup();
        if (startup != 0)
        {
            Log(WorldConnectionOpenLog.SocketStartupFailed, unchecked((uint)startup));
            return CloseAndReturn();
        }
        memory.WriteWord(8, services.CreateMutex());
        memory.WriteWord(0xc, services.CreateMutex());
        memory.WriteWord(0x10, services.CreateMutex());
        Log(WorldConnectionOpenLog.Opening, port);
        services.InspectEnvironment();
        var socket = services.Sockets.Create();
        memory.WriteWord(0x14, socket);
        if (socket == uint.MaxValue)
        {
            Log(WorldConnectionOpenLog.SocketCreatedFailed);
            memory.WriteWord(0x14, uint.MaxValue);
            return CloseAndReturn();
        }
        if (services.Sockets.SetReuseAddress(socket) == -1)
        {
            Log(WorldConnectionOpenLog.SocketOptionFailed, services.Sockets.LastError());
            return CloseAndReturn();
        }
        if (services.Sockets.Bind(memory.ReadWord(0x14), port) == -1)
        {
            Log(WorldConnectionOpenLog.BindFailed, services.Sockets.LastError());
            return CloseAndReturn();
        }
        Log(WorldConnectionOpenLog.SocketBound, port);
        var transport = services.Transport.Create();
        memory.WriteWord(0x24, transport);
        if (transport == 0) throw new NotSupportedException("Original open dereferences a null transport on this failure path.");
        Log(WorldConnectionOpenLog.TransportCreated, services.Transport.GetBuildNumber());
        // Providers own executable-derived log-channel arguments for these calls.
        if (!services.Transport.InitializeLogging(memory.ReadWord(0x24))) Log(WorldConnectionOpenLog.TransportLogFailed);
        if (services.ReadRuntimeFlag() == 0) Call(0x4c, 0);
        Call(0x2c, 300);
        if (Call(8, 2, 0x26b7, port, 0, 1) == 0)
        {
            Log(WorldConnectionOpenLog.TransportInitializeFailed, Call(0x18));
            ReleaseTransport(); return Result();
        }
        Call(0x10, services.ConnectionAddress);
        var values = services.Transport.CreateValues();
        if (values == 0)
        {
            Log(WorldConnectionOpenLog.ValuesCreateFailed);
            ReleaseTransport(); return Result();
        }
        Configure(8, 0, 2, 0x26b7, port);
        Configure(0x2c, 5); Configure(0x34, 5); Configure(0x3c, 100);
        Configure(0x44, 15000); Configure(0x4c, 10000); Configure(0x54, 70000); Configure(0x5c, 30000);
        Configure(0x68, memory.ReadWord(0x40414) == 2 ? 1u : 0);
        if (Call(0x38, values) == 0)
        {
            Log(WorldConnectionOpenLog.ValuesApplyFailed, Call(0x18));
            ReleaseTransport(); return Result();
        }
        memory.WriteWord(0x18, 1);
        memory.WriteByte(0x2a, 1);
        if (mode != 0)
        {
            var backend = services.CreateOptionalBackend();
            services.WriteOptionalBackend(backend);
            if (backend != 0)
            {
                if (services.OptionalHasConnection(backend)) memory.WriteByte(0x20, mode);
                else Log(WorldConnectionOpenLog.OptionalBackendFailed);
            }
        }
        return Result();

        byte Result() => memory.ReadByte(0x2a);
        byte CloseAndReturn() { services.Close(); return Result(); }
        void Log(WorldConnectionOpenLog stage, uint value = 0) => services.Log(stage, value);
        uint Call(uint offset, params uint[] arguments)
        {
            var current = memory.ReadWord(0x24);
            if (current == 0) throw new NotSupportedException("Native open dereferences a removed current transport.");
            return services.Transport.Invoke(current, offset, arguments);
        }
        void Configure(uint offset, params uint[] arguments) => services.Transport.Invoke(values, offset, arguments);
        void ReleaseTransport() { Call(4); memory.WriteWord(0x24, 0); }
    }
}
