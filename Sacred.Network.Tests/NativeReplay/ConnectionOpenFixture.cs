using System.Buffers.Binary;
using System.Text.Json;
using Sacred.GameplayHost.Networking;

internal sealed class ConnectionOpenFixture
{
    private const uint Connection = 0x6000000, Primary = 0x6080000, Alternate = 0x6081000, Values = 0x6082000, Optional = 0x6083000, Records = 0x6090000;
    private readonly JsonElement _row;
    private readonly byte[] _memory = Enumerable.Repeat((byte)0xa5, 0x40468).ToArray();
    private int _mutex;
    private uint _firstCount, _secondCount;
    public List<object[]> Events { get; } = [];
    public WorldControlMemory Memory { get; }
    public WorldConnectionOpenServices Services { get; }
    public uint Backend { get; private set; } = 0x11111111;
    public byte[] Header => _memory[..0x50];
    public byte[] ResetBytes => _memory.AsSpan(4, 0x40410).ToArray();
    private string Failure => _row.GetProperty("failure").GetString()!;
    private string Mutation => _row.GetProperty("mutation").GetString()!;
    private string OptionalProfile => _row.GetProperty("optional").GetString()!;
    public ConnectionOpenFixture(JsonElement row)
    {
        _row = row.Clone();
        Memory = new(at => _memory[at], (at, value) => _memory[at] = value,
            at => BinaryPrimitives.ReadUInt32LittleEndian(_memory.AsSpan((int)at)),
            (at, value) => BinaryPrimitives.WriteUInt32LittleEndian(_memory.AsSpan((int)at), value),
            (at, value) => BinaryPrimitives.WriteUInt16LittleEndian(_memory.AsSpan((int)at), value));
        Memory.WriteByte(0x2a, row.GetProperty("active").GetByte()); Memory.WriteWord(0x40414, 0);
        var count = row.GetProperty("count").GetUInt32();
        _firstCount = _secondCount = count;
        Memory.WriteWord(0x4044c, Records); Memory.WriteWord(0x40450, Records + count * 52); Memory.WriteWord(0x40454, Records + count * 52);
        var sockets = new WorldConnectionOpenSockets(() => { Events.Add(["wsa", 0x202]); return Failure == "wsa" ? 100 : 0; },
            () => { Events.Add(["socket", 2, 2, 0]); return Failure == "socket" ? uint.MaxValue : 55u; },
            socket => { Events.Add(["option", socket, 65535, 4, 1, 4]); return Failure == "option" ? -1 : 0; },
            (socket, port) =>
            {
                Events.Add(["htons", port]); Events.Add(["htonl", 0]);
                var address = new byte[16]; address[0] = 2; BinaryPrimitives.WriteUInt16BigEndian(address.AsSpan(2), (ushort)port);
                Events.Add(["bind", socket, Convert.ToHexString(address).ToLowerInvariant(), 16]); return Failure == "bind" ? -1 : 0;
            }, () => { Events.Add(["last-error"]); return 1234; });
        var transport = new WorldConnectionOpenTransport(() => { Events.Add(["create-primary"]); return Primary; },
            () => { Events.Add(["build"]); return 5; }, () => { Events.Add(["create-rate"]); return Failure == "rate-null" ? 0u : Values; },
            Invoke, instance => Invoke(instance, 0x50, new uint[] { 0xa181fc }) != 0);
        Services = new(Memory, Connection, () => { Events.Add(["close"]); Memory.WriteByte(0x2a, Mutation == "close-result" ? (byte)0x7f : (byte)0); },
            () =>
            {
                var currentCount = (Memory.ReadWord(0x40450) - Memory.ReadWord(0x4044c)) / 52;
                if (currentCount == 0) return;
                Events.Add(["clear-records"]);
                for (uint index = 0; index < currentCount; index++) Events.Add(["destroy-record", index]);
                Memory.WriteWord(0x40450, Records);
            }, () => ClearQueue(0), () => ClearQueue(1), sockets,
            () => { Events.Add(["mutex", 0, 0, 0]); return (uint)(100 + ++_mutex); },
            () => { Events.Add(["file", new uint[] { 0x80000000, 1, 0, 3, 0, 0 }]); Events.Add(["log", "0xa18300"]); },
            (stage, _) =>
            {
                var address = LogAddress(stage); Events.Add(["log", "0x" + address.ToString("x")]);
                if (stage == WorldConnectionOpenLog.TransportCreated && Mutation == "replace-primary") Memory.WriteWord(0x24, Alternate);
            }, transport, () => row.GetProperty("runtime_flag").GetByte(), () =>
            {
                Events.Add(["allocate-optional"]);
                if (OptionalProfile == "allocation-null") return 0;
                Events.Add(["construct-optional"]); return OptionalProfile == "constructor-null" ? 0u : Optional;
            }, backend => Backend = backend, _ =>
            {
                Events.Add(["query-optional"]);
                if (Mutation == "active-result") Memory.WriteByte(0x2a, 0xa5);
                var backend = new WorldControlMemory(_ => 0, (_, _) => { },
                    at => at == 0xc ? (OptionalProfile == "query-false" ? 0u : 1u) : throw new Exception("Unexpected optional field."),
                    (_, _) => { }, (_, _) => { });
                return WorldConnectionOpen.HasOptionalConnection(backend);
            });

        void ClearQueue(uint index)
        {
            var currentCount = index == 0 ? _firstCount : _secondCount;
            for (uint node = 1; node <= currentCount; node++) Events.Add(["free-node", 0x6091000 + index * 0x100 + node * 16]);
            if (index == 0) _firstCount = 0; else _secondCount = 0;
        }
    }

    public WorldConnectionCloseServices CreateCloseServices() => new(Memory, () => Backend,
        target => Events.Add(["destroy-optional", target]), value => Backend = value, () => Events.Add(["clear-connection"]),
        handle => Events.Add(["close-handle", handle]), socket => Events.Add(["shutdown-socket", socket]),
        socket => Events.Add(["close-socket", socket]), () => Events.Add(["cleanup-sockets"]),
        (target, offset) => Invoke(target, offset, Array.Empty<uint>()), Services.ClearFirstQueue, () =>
        {
            for (uint node = 1; node <= _secondCount; node++) Events.Add(["erase-node", 0x6091100 + node * 16]);
            _secondCount = 0;
        }, Services.ClearPendingRecords, () => Events.Add(["closed"]));
    private uint Invoke(uint instance, uint offset, IReadOnlyList<uint> arguments)
    {
        var label = instance == Values ? "rate" : instance == Alternate ? "alternate" : "primary";
        Events.Add(new object[] { label, offset }.Concat(arguments.Select(argument => (object)argument)).ToArray());
        if (label == "rate" && offset == 0x5c && Mutation == "speed") Memory.WriteWord(0x40414, 2);
        if (offset == 0x18) return 7;
        if (label != "rate" && offset == 8) return Failure == "primary-init" ? 0u : 1;
        if (label != "rate" && offset == 0x38) return Failure == "rate-apply" ? 0u : 1;
        if (label != "rate" && offset == 0x50) return Failure == "log-init" ? 0u : 1;
        return 1;
    }
    private static uint LogAddress(WorldConnectionOpenLog stage) => stage switch
    {
        WorldConnectionOpenLog.SocketStartupFailed => 0xa18358, WorldConnectionOpenLog.Opening => 0xa18334,
        WorldConnectionOpenLog.SocketCreatedFailed => 0xa180a8, WorldConnectionOpenLog.SocketOptionFailed => 0xa182c0,
        WorldConnectionOpenLog.BindFailed => 0xa18290, WorldConnectionOpenLog.SocketBound => 0xa18274,
        WorldConnectionOpenLog.TransportCreated => 0xa18210, WorldConnectionOpenLog.TransportLogFailed => 0xa181c4,
        WorldConnectionOpenLog.TransportInitializeFailed => 0xa1818c, WorldConnectionOpenLog.ValuesApplyFailed => 0xa18144,
        WorldConnectionOpenLog.ValuesCreateFailed => 0xa180d4, WorldConnectionOpenLog.OptionalBackendFailed => 0xa18110,
        _ => throw new ArgumentOutOfRangeException(nameof(stage))
    };
}
