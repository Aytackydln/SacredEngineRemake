using System.Buffers.Binary;
using System.Text.Json;
using Sacred.GameplayHost.Networking;
using Sacred.Network.Gameplay;

internal sealed class Controller117Fixture
{
    private readonly JsonElement _input;
    private readonly Memory _controller = new(), _connection = new();
    private readonly Ring _first, _second;
    private readonly List<string> _events = [];
    private readonly List<GoldLobbyUiRequest> _ui = [];
    private bool _connectionPresent = true;
    private int _statusCalls;
    public WorldController117Services Services { get; }
    public IReadOnlyList<string> Events => _events;
    public IReadOnlyList<GoldLobbyUiRequest> Ui => _ui;
    public bool ConnectionPresent => _connectionPresent;
    public byte[] Header => _controller.Bytes(0, 0x50);
    public byte[] Tail => _controller.Bytes(0x3818, 6);
    public byte[] ConnectionHeader => _connection.Bytes(0, 0x50);
    public uint Word(uint at) => _connection.ReadWord(at);
    public uint PendingCount => (_controller.ReadWord(0x3a58) - _controller.ReadWord(0x3a54)) / 4;
    public string TextFirst => Convert.ToHexString(_connection.Bytes(0x6079000, 2)).ToLowerInvariant();
    public IReadOnlyList<uint> FirstPayloads => _first.Payloads();
    public IReadOnlyList<uint> SecondPayloads => _second.Payloads();

    public Controller117Fixture(JsonElement row)
    {
        _input = row.GetProperty("inputs").Clone();
        var before = row.GetProperty("before");
        _controller.Load(0, Convert.FromHexString(before.GetProperty("header").GetString()!));
        _controller.Load(0x3818, Convert.FromHexString(before.GetProperty("tail").GetString()!));
        _controller.WriteWord(0x3a54, 0x6078000); _controller.WriteWord(0x3a58, 0x607800c);
        _connection.Load(0, Convert.FromHexString(before.GetProperty("connection_header").GetString()!));
        foreach (var word in before.GetProperty("words").EnumerateArray()) _connection.WriteWord(word[0].GetUInt32(), word[1].GetUInt32());
        _connection.WriteHalf(0x6079000, 65);
        var count = _input.GetProperty("count").GetInt32();
        _first = new(count, index =>
        {
            _events.Add("free:0:" + index);
            if (Mutation == "truncate" && index == 0 && count == 3) _first!.TruncateAfterSecond();
        });
        _second = new(count, index => _events.Add("free:1:" + index));
        var connection = new WorldConnectionClearServices(_connection.Adapter, session =>
        {
            _events.Add("release:" + session);
            if (Mutation == "release-process") _controller.WriteByte(0x1c, 1);
        }, () => _events.Add("log-close"), () =>
        {
            var begin = _connection.ReadWord(0x40438);
            if (begin == _connection.ReadWord(0x4043c)) return;
            _connection.WriteHalf(begin, 0); _connection.WriteWord(0x4043c, begin);
        }, _first.Services(() => Enter(0x1010), () => Exit(0x1010)), _second.Services(() => Enter(0x2020), () => Exit(0x2020)));
        var process = new WorldHostedServerProcessServices(window =>
        {
            _events.Add("post:" + window);
            if (Mutation == "signal-handle") _controller.WriteWord(0x20, 0x5555);
        }, name =>
        {
            if (name != "SACREDGAMESERVERCLASS") throw new Exception("wrong process window class");
            _events.Add("find"); return ProcessProfile == 1 ? 0x4444u : 0;
        }, handle => _events.Add("terminate:" + handle), handle =>
        {
            WorldHostedServerExitStatus[] sequence = ProcessProfile switch
            {
                0 => [new(true, 0)], 1 => [new(true, 259), new(true, 259), new(true, 0)],
                2 => [new(false, 259)], _ => [new(true, 259), new(false, 259)]
            };
            var status = sequence[Math.Min(_statusCalls++, sequence.Length - 1)];
            _events.Add($"status:{handle}:{(status.Success ? 1 : 0)}:{status.Code}"); return status;
        }, delay => _events.Add("sleep:" + delay));
        var dispatcher = new WorldAdmissionUiDispatcher(() => 1, _ => request =>
        {
            _ui.Add(request); _events.Add("ui");
            if (Mutation == "ui-state")
            {
                _controller.WriteByte(1, 7); _controller.WriteByte(0x28, 8);
                _controller.WriteWord(0x14, 9); _controller.WriteWord(0x24, 10);
            }
        });
        var backend = new object();
        var pending = 1;
        var pump = new WorldConnectionBackendPumpServices<object>(() => BackendPresent ? backend : null,
            _ => _events.Add("pump"), _ => Enter(0x3030), _ => Exit(0x3030), _ => pending,
            (_, _) => 0x7777, _ => pending = 0, () => Enter(0x1010), () => Exit(0x1010),
            value => _first.Append(value), _ => throw new Exception("wrong backend query service"));
        Services = new(_controller.Adapter, () => _connectionPresent ? connection : null, process,
            () => _controller.WriteWord(0x3a58, _controller.ReadWord(0x3a54)), () =>
            {
                if (!BackendPresent) return false;
                _events.Add("query");
                if (Mutation == "query-role") _controller.WriteByte(0x381b, (byte)(_controller.ReadByte(0x381b) == 0 ? 1 : 0));
                else if (Mutation == "query-remove") _connectionPresent = false;
                return _input.GetProperty("query").GetBoolean();
            }, (current, command) =>
            {
                if (!ReferenceEquals(current, connection)) throw new Exception("command used stale connection");
                WorldConnectionBackendPump.PumpAndCommand(pump, command, (_, value) => { _events.Add("command:" + value); return true; });
            }, dispatcher.Dispatch, key => _events.Add("chat:" + key));
    }

    private string? Mutation => _input.GetProperty("mutation").GetString();
    private int ProcessProfile => _input.GetProperty("process_profile").GetInt32();
    private bool BackendPresent => _input.GetProperty("backend").GetBoolean();
    private void Enter(uint handle) => _events.Add("enter:" + handle);
    private void Exit(uint handle) => _events.Add("exit:" + handle);

    /// <summary>Only the original zeroed fixture backing is implicit here;
    /// production storage adapters must establish their own backing.</summary>
    private sealed class Memory
    {
        private readonly Dictionary<uint, byte> _bytes = [];
        public WorldControlMemory Adapter => new(ReadByte, WriteByte, ReadWord, WriteWord, WriteHalf);
        public byte ReadByte(uint at) => _bytes.GetValueOrDefault(at);
        public void WriteByte(uint at, byte value) => _bytes[at] = value;
        public uint ReadWord(uint at) => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(at, 4));
        public void WriteWord(uint at, uint value)
        { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); Load(at, bytes); }
        public void WriteHalf(uint at, ushort value)
        { Span<byte> bytes = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(bytes, value); Load(at, bytes); }
        public void Load(uint at, ReadOnlySpan<byte> bytes)
        { for (var i = 0; i < bytes.Length; i++) WriteByte(at + (uint)i, bytes[i]); }
        public byte[] Bytes(uint at, int count) => Enumerable.Range(0, count).Select(index => ReadByte(at + (uint)index)).ToArray();
    }

    private sealed class Ring
    {
        private const uint Head = 100;
        private sealed class Node(uint next, uint previous, uint payload)
        { public uint Next = next, Previous = previous, Payload = payload; }
        private readonly Dictionary<uint, Node> _nodes = [];
        private readonly Action<int> _free;
        private uint _nextAllocation = 200;
        public Ring(int count, Action<int> free)
        {
            _free = free;
            var links = new[] { Head }.Concat(Enumerable.Range(0, count).Select(index => Head + 1 + (uint)index)).ToArray();
            for (var i = 0; i < links.Length; i++)
                _nodes[links[i]] = new(links[(i + 1) % links.Length], links[(i + links.Length - 1) % links.Length], 0x7000 + (uint)i);
        }
        public WorldConnectionQueueClearServices Services(Action enter, Action exit) => new(() => Head, at => _nodes[at].Next,
            (at, value) => _nodes[at].Next = value, (at, value) => _nodes[at].Previous = value,
            at => _free(checked((int)(at - Head - 1))), enter, exit);
        public void TruncateAfterSecond() => _nodes[Head + 2].Next = Head;
        public void Append(uint payload)
        {
            var at = _nextAllocation++; var previous = _nodes[Head].Previous;
            _nodes[at] = new(Head, previous, payload); _nodes[previous].Next = at; _nodes[Head].Previous = at;
        }
        public IReadOnlyList<uint> Payloads()
        {
            var output = new List<uint>(); var node = _nodes[Head].Next;
            while (node != Head) { output.Add(_nodes[node].Payload); node = _nodes[node].Next; }
            return output;
        }
    }
}
