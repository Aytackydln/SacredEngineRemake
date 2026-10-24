using System.Globalization;
using System.Text;

namespace Sacred.Network.Gameplay;

/// <summary>Inputs read from native controller+381B and globals 182EE4C/182EE50. Bit meanings remain unnamed.</summary>
public sealed record GoldLobbyChannelFilter(bool ControllerFlag, uint GlobalFlags, bool GlobalByte)
{
    public bool Includes(uint flags) =>
        ControllerFlag && (flags & 1) == 0 || !ControllerFlag && (flags & 1) != 0 &&
        (GlobalByte == ((flags & 0x100) != 0) ||
         (GlobalFlags & 0x4000) != 0 && (flags & 0x800) != 0 ||
         (GlobalFlags & 0x2000) != 0 && (flags & 0x400) != 0 ||
         (GlobalFlags & 0x1000) != 0 && (flags & 0x200) != 0);
}

public sealed record GoldLobbyChannelView(int SourceIndex, GoldLobbyChannelRow Row, string? Name)
{
    public string? Text => Name is null ? null : string.Create(CultureInfo.InvariantCulture,
        $"#{SourceIndex + 1} {Name} ({Row.PlayerCount}/{Row.PlayerLimit})");
}

/// <summary>Receive state and presentation requests. A menu consumer supplies its local filter and ANSI code page.</summary>
public sealed class GoldLobbyReplica
{
    private GoldLobbyChannelsEvent? _channels;
    private Encoding? _nameEncoding;
    public GoldLobbyControlReplica Control { get; } = new();
    public GoldLobbyProfileTransfers Transfers { get; } = new();
    public GoldBuddyReplica Buddies { get; } = new();
    public GoldLobbyProfileEvent? LastProfile { get; private set; }
    public GoldLobbyServerReplica ServerBrowser { get; } = new();
    public GoldLobbyWindowEvent? LastWindowRequest { get; private set; }
    /// <summary>Exact word written to native controller+30 by 02D; its broader service meaning is unmapped.</summary>
    public uint? NetworkWindowValue { get; private set; }
    public GoldLobbyChannelFilter? ChannelFilter { get; private set; }
    public IReadOnlyList<GoldLobbyChannelRow> Channels => _channels?.Rows ?? Array.Empty<GoldLobbyChannelRow>();
    /// <summary>Null until the menu supplies native filter inputs. Empty means a known empty selection.</summary>
    public IReadOnlyList<GoldLobbyChannelView>? ChannelRequests { get; private set; }
    public long ReceivedCount { get; private set; }
    public long ChannelClearRequestCount { get; private set; }
    public long ProfileRequestCount { get; private set; }
    public long WindowRequestCount { get; private set; }

    public void ConfigureChannels(GoldLobbyChannelFilter filter, Encoding? nameEncoding = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ChannelFilter = filter;
        _nameEncoding = nameEncoding is null ? null : (Encoding)nameEncoding.Clone();
        ResolveChannels();
    }

    internal void Apply(GoldLobbyEvent entry)
    {
        if (entry is GoldBuddyEvent buddy) { Buddies.Apply(buddy); ReceivedCount++; return; }
        if (entry is GoldLobbyServerEvent or GoldLobbyPlayerRefreshEvent)
        {
            ServerBrowser.Apply(entry); ReceivedCount++; return;
        }
        // Wire data wins over caller-supplied record fields, before any receive state changes.
        var decoded = GoldLobbyCodec.Decode(entry.Message);
        switch (decoded)
        {
            case GoldLobbyProfileEvent profile: LastProfile = profile; ProfileRequestCount++; break;
            case GoldLobbyWindowEvent window:
                LastWindowRequest = window; WindowRequestCount++;
                if (window.Message.Opcode == GoldLobbyCodec.WindowOpcode)
                { NetworkWindowValue = window.Value; ServerBrowser.WindowValue = window.Value; }
                break;
            case GoldLobbyChannelsEvent channels:
                _channels = channels; ChannelClearRequestCount++; ResolveChannels(); break;
        }
        ReceivedCount++;
    }

    private void ResolveChannels()
    {
        if (_channels is null || ChannelFilter is null) { ChannelRequests = null; return; }
        var rows = new List<GoldLobbyChannelView>();
        for (var i = 0; i < _channels.Rows.Count; i++)
        {
            var row = _channels.Rows[i];
            if (!ChannelFilter.Includes(row.Flags)) continue;
            // Gold uses MultiByteToWideChar(CP_ACP). Do not substitute UTF-8 or guess an ANSI locale.
            var bytes = row.NameBytes.Span;
            var name = _nameEncoding?.GetString(bytes);
            if (name is null && !bytes.ContainsAnyInRange((byte)128, byte.MaxValue)) name = Encoding.ASCII.GetString(bytes);
            rows.Add(new(i, row, name));
        }
        ChannelRequests = rows.AsReadOnly();
    }

    internal void Clear()
    {
        ServerBrowser.Clear(); Buddies.Clear(); Control.Clear(); Transfers.Clear();
        _channels = null; LastProfile = null; LastWindowRequest = null; NetworkWindowValue = null; ChannelRequests = null;
        ReceivedCount = ChannelClearRequestCount = ProfileRequestCount = WindowRequestCount = 0;
    }
}
