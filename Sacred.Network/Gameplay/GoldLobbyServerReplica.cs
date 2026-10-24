using System.Text;

namespace Sacred.Network.Gameplay;

public sealed record GoldLobbyServerView(GoldLobbyServerRecord Record, string? Name);

/// <summary>Native server browser vector, current record, and receipt-time profile refresh requests.</summary>
public sealed class GoldLobbyServerReplica
{
    private readonly List<GoldLobbyServerView> _servers = [];
    private Encoding? _nameEncoding;
    public GoldLobbyServerReplica() => Servers = _servers.AsReadOnly();
    public IReadOnlyList<GoldLobbyServerView> Servers { get; }
    public GoldLobbyServerView? Current { get; private set; }
    /// <summary>Native controller+30, supplied by 02D. Zero is the native initial value.</summary>
    public uint WindowValue { get; internal set; }
    /// <summary>Native online-manager+2B gate, supplied by the lobby consumer.</summary>
    public bool SuppressPlayerRefresh { get; set; }
    public GoldLobbyPlayerRefreshEvent? LastPlayerRefresh { get; private set; }
    public long RefreshRequestCount { get; private set; }
    /// <summary>030 queues a native kind-10 request with flags 0x20000 after both presentation events.</summary>
    public uint? LastProfileRequestValue => LastPlayerRefresh?.Value;
    public long Revision { get; private set; }
    public bool ListChanged { get; private set; }

    public void ConfigureNameEncoding(Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        _nameEncoding = (Encoding)encoding.Clone();
        for (var i = 0; i < _servers.Count; i++) _servers[i] = View(_servers[i].Record);
        if (Current is { } current) Current = View(current.Record);
    }

    internal void Apply(GoldLobbyEvent entry)
    {
        var decoded = GoldLobbyServerCodec.Decode(entry.Message);
        if (decoded is GoldLobbyPlayerRefreshEvent refresh)
        {
            if (!SuppressPlayerRefresh) { LastPlayerRefresh = refresh; RefreshRequestCount++; }
            return;
        }
        var server = ((GoldLobbyServerEvent)decoded).Server;
        if (entry.Message.Opcode == GoldLobbyServerCodec.CurrentOpcode) { Current = View(server); Revision++; return; }
        if (entry.Message.Opcode == GoldLobbyServerCodec.UpsertOpcode && server.WindowValue != WindowValue) return;
        if (_servers.Count > 200) _servers.Clear();
        if (entry.Message.Opcode == GoldLobbyServerCodec.UpsertOpcode)
        {
            var index = _servers.FindIndex(row => row.Record.EntryKey == server.EntryKey);
            if (index < 0) _servers.Add(View(server));
            else _servers[index] = View(server);
        }
        else
        {
            // Native compares the decoded name and word zero, and erases only the first exact match.
            var name = DecodeName(server.NameBytes.Span);
            var index = _servers.FindIndex(row => row.Record.Words[0] == server.Words[0] &&
                (name is not null && row.Name is not null ? row.Name == name : row.Record.NameBytes.Span.SequenceEqual(server.NameBytes.Span)));
            if (index >= 0) _servers.RemoveAt(index);
        }
        ListChanged = true; Revision++;
    }

    private GoldLobbyServerView View(GoldLobbyServerRecord record) => new(record, record.UnicodeName ?? DecodeName(record.NameBytes.Span));
    private string? DecodeName(ReadOnlySpan<byte> bytes) => _nameEncoding?.GetString(bytes) ??
        (bytes.ContainsAnyInRange((byte)128, byte.MaxValue) ? null : Encoding.ASCII.GetString(bytes));

    internal void Clear()
    {
        _servers.Clear(); Current = null; LastPlayerRefresh = null; WindowValue = 0;
        RefreshRequestCount = Revision = 0; ListChanged = false;
    }
}
