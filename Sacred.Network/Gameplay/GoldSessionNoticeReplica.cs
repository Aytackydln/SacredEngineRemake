using Sacred.Core.GameRes;

namespace Sacred.Network.Gameplay;

public sealed record GoldSessionNoticeView(GoldSessionNoticeEvent Event, string? Text, uint? DialogAction)
{
    public uint NativeDialogParameter => Event.Message.Opcode == GoldSessionNoticeCodec.LobbyKickOpcode || Event.EndsSession ? 3u : 0u;
    public bool NeedsLocalization => Text is null;
    public uint? NativeChatSelector => Event.ReportsConnectionLoss ? 0u : Event.IsModal ? null :
        Event.Message.Opcode == GoldSessionNoticeCodec.BuddyAddedOpcode ? 17u : 0u;
}

/// <summary>Received presentation requests for DEFAULT_BUSYDLG and system chat. Rendering is a separate consumer.</summary>
public sealed class GoldSessionNoticeReplica
{
    private readonly List<GoldSessionNoticeView> _recent = [];
    private GameResStore _resources = GameResStore.Empty;
    private GoldSessionNoticeEvent? _customTextSource;
    public GoldSessionNoticeReplica() => Recent = _recent.AsReadOnly();
    public IReadOnlyList<GoldSessionNoticeView> Recent { get; }
    public GoldSessionNoticeView? LastReceipt { get; private set; }
    public GoldSessionNoticeView? CurrentDialog { get; private set; }
    /// <summary>Last received replacement of the native mutable text resource 0x26FF, including nonmodal lobby notices.</summary>
    public string? CustomDialogText { get; private set; }
    public long ReceivedCount { get; private set; }
    public long CloseRequestCount { get; private set; }
    public long OpenRequestCount { get; private set; }
    public long ChatRequestCount { get; private set; }
    public long ConnectionLostChatRequestCount { get; private set; }
    public long SessionEndRequestCount { get; private set; }

    /// <summary>Use original localization data; late availability resolves retained requests without replaying their state changes.</summary>
    public void ConfigureResources(GameResStore resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        if (ReferenceEquals(resources, _resources)) return;
        _resources = resources;
        for (var i = 0; i < _recent.Count; i++) _recent[i] = Resolve(_recent[i].Event);
        if (LastReceipt is { } last) LastReceipt = Resolve(last.Event);
        if (CurrentDialog is { } dialog) CurrentDialog = Resolve(dialog.Event);
        if (_customTextSource is { } custom) CustomDialogText = Resolve(custom).Text;
    }

    internal void Apply(GoldSessionNoticeEvent entry)
    {
        var decoded = GoldSessionNoticeCodec.Decode(entry.Message); var view = Resolve(decoded);
        ReceivedCount++; LastReceipt = view; _recent.Add(view);
        if (decoded.EndsSession) SessionEndRequestCount++;
        if (decoded.ReportsConnectionLoss) { ConnectionLostChatRequestCount++; ChatRequestCount++; }
        if (_recent.Count > GoldChatReplica.HistoryLimit) _recent.RemoveAt(0);
        if (decoded.ClearsBusyDialog) { CurrentDialog = null; CloseRequestCount++; }
        if (decoded.UpdatesCustomText) { _customTextSource = decoded; CustomDialogText = view.Text; }
        if (decoded.IsModal) { CurrentDialog = view; OpenRequestCount++; }
        else ChatRequestCount++;
    }

    private GoldSessionNoticeView Resolve(GoldSessionNoticeEvent entry)
    {
        string? localized = null;
        if (entry.LocalizationKey is { } key) _resources.Strings.TryGetValue(SacredResourceHash.Compute(key), out localized);
        var body = entry.Message.Opcode switch
        {
            GoldSessionNoticeCodec.BuddyAddedOpcode => localized is null ? null : localized + ": " + entry.Text,
            GoldSessionNoticeCodec.LobbyKickOpcode or GoldSessionNoticeCodec.ConnectionFailedOpcode => localized is null ? null : localized + "\n\n" + entry.Text,
            GoldSessionNoticeCodec.ConnectionAbortedOpcode => localized,
            GoldSessionNoticeCodec.ConnectionClosedOpcode or GoldSessionNoticeCodec.EndSessionOpcode or
                GoldSessionNoticeCodec.ConnectionLostOpcode or GoldSessionNoticeCodec.ConnectionLostAlternateOpcode => localized,
            GoldSessionNoticeCodec.ServerNoticeOpcode => entry.Text,
            GoldSessionNoticeCodec.LobbyNoticeOpcode => "Lobby: " + entry.Text,
            _ => throw new InvalidDataException("Unexpected Gold session notice.")
        };
        uint? action = null;
        if (entry.IsModal)
        {
            if (entry.Message.Opcode is GoldSessionNoticeCodec.ConnectionAbortedOpcode or GoldSessionNoticeCodec.LobbyKickOpcode or
                GoldSessionNoticeCodec.ConnectionClosedOpcode || entry.EndsSession) action = 1;
            else if (body is not null) action = body.Length > 150 ? 7u : 1u;
        }
        return new(entry, body, action);
    }

    internal void Clear()
    {
        _recent.Clear(); LastReceipt = CurrentDialog = null; _customTextSource = null; CustomDialogText = null;
        ReceivedCount = CloseRequestCount = OpenRequestCount = ChatRequestCount = 0;
        ConnectionLostChatRequestCount = SessionEndRequestCount = 0;
    }
}
