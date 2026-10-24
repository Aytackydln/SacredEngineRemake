using System.Globalization;
using Sacred.Core.GameRes;

namespace Sacred.Network.Gameplay;

/// <summary>Receive-time lobby/session decisions. Local service inputs must be supplied explicitly.</summary>
public sealed class GoldLobbyControlReplica
{
    private readonly List<GoldLobbyControlNotice> _notices = [];
    private readonly List<GoldLobbyUiRequest> _ui = [];
    private readonly List<GoldLocalDepartureWork> _departures = [];
    private readonly List<GoldLobbyResultEvent> _conditionalResults = [];
    private readonly List<uint> _channelQueries = [];
    private GameResStore _resources = GameResStore.Empty;
    public GoldLobbyControlReplica()
    {
        Notices = _notices.AsReadOnly(); UiRequests = _ui.AsReadOnly();
        LocalDepartures = _departures.AsReadOnly(); ConditionalResults = _conditionalResults.AsReadOnly(); ChannelQueries = _channelQueries.AsReadOnly();
    }
    public GoldSessionRosterReplica Roster { get; } = new();
    public GoldGameSettingsReplica Settings { get; } = new();
    public IReadOnlyList<GoldLobbyControlNotice> Notices { get; }
    public IReadOnlyList<GoldLobbyUiRequest> UiRequests { get; }
    public IReadOnlyList<GoldLocalDepartureWork> LocalDepartures { get; }
    public IReadOnlyList<GoldLobbyResultEvent> ConditionalResults { get; }
    public IReadOnlyList<uint> ChannelQueries { get; }
    public long ReceivedCount { get; private set; }
    public long SingletonListClearCount { get; private set; }
    public long Native117RequestCount { get; private set; }
    public long ClockResetRequestCount { get; private set; }
    public long ServiceSettingRequestCount { get; private set; }
    public long OwnProfileRequestCount { get; private set; }
    public bool? NativeFlag3819 { get; private set; }
    public bool? NativeFlag381A { get; private set; }
    public bool? NativeFlag381B { get; private set; }
    public bool? NativeFlag381C { get; private set; }
    public bool? LocalDepartureClosesBusy { get; private set; }
    public void ConfigureService(bool flag381B, bool flag381C) { NativeFlag381B = flag381B; NativeFlag381C = flag381C; }
    public void ConfigureLocalDeparture(bool singletonReady, bool flag28) => LocalDepartureClosesBusy = singletonReady && flag28;
    public void ConfigureResources(GameResStore resources) => _resources = resources ?? throw new ArgumentNullException(nameof(resources));
    internal void Apply(GoldLobbyControlEvent incoming, GoldSessionState session)
    {
        var entry = GoldLobbyControlCodec.Decode(incoming.Message); ReceivedCount++;
        var name = Roster.Name(entry.Message.Sender);
        switch (entry)
        {
            case GoldGameSettingsEvent settings: Settings.Apply(settings); break;
            case GoldServiceErrorEvent error:
                var chat = error.ErrorCode is 21 or 22 or 107;
                var notice = new GoldLobbyControlNotice(error, name, chat, chat ? null : error.ErrorCode == 108 ? 7u : 1u,
                    GoldServiceErrorKeys.Get(error.ErrorCode), string.IsNullOrEmpty(name) ? "UI_SPIELER" : null);
                _notices.Add(notice);
                if (!chat) _ui.Add(new(0x39, "DEFAULT_BUSYDLG", 2, notice.DialogAction!.Value));
                break;
            case GoldPlayerSessionEvent player:
                if (!player.IsLogin && player.Message.Sender == session.Teams.LocalPlayerSlot)
                {
                    SingletonListClearCount++; _departures.Add(new(player, LocalDepartureClosesBusy));
                    if (LocalDepartureClosesBusy == true) { CloseBusy(); ClockResetRequestCount++; }
                    else if (LocalDepartureClosesBusy == false) Native117RequestCount++;
                    break;
                }
                _notices.Add(new(player, name, true, null, player.IsLogin ? "UI_NET_GS_LOGIN" :
                    player.Reason is 9 or 10 ? "UI_NET_ENDGAME_CRASH" : "UI_NET_ENDGAME", string.IsNullOrEmpty(name) ? "UI_SPIELER" : null));
                if (!player.IsLogin)
                {
                    session.Teams.RemoveDepartedPlayer(player.Message.Sender);
                    session.ChannelChat.ClearPlayer(player.Message.Sender);
                    Roster.ClearPlayer(player.Message.Sender);
                }
                break;
            case GoldLobbyResultEvent result: ApplyResult(result, session.Lobby.NetworkWindowValue); break;
        }
    }
    private void CloseBusy() => _ui.Add(new(0x39, "DEFAULT_BUSYDLG", 2, 4));
    private void ApplyResult(GoldLobbyResultEvent result, uint? windowValue)
    {
        switch (result.ResultCode)
        {
            case 0 when result.Message.Opcode == GoldLobbyControlCodec.LoginResultOpcode:
                NativeFlag3819 = NativeFlag381A = false; SingletonListClearCount++;
                CloseBusy(); _ui.Add(new(0x36, "UI_WND_NETWORK", 1, 5));
                if (NativeFlag381C == false) { NativeFlag381C = true; ServiceSettingRequestCount++; }
                if (NativeFlag381B == false) for (uint i = 1; i <= 8; i++) _channelQueries.Add(i);
                break;
            case 0 when result.Selector == 1:
                _ui.Add(new(0x36, "UI_WND_NETWORK", 1, 1)); CloseBusy();
                _ui.Add(new(0x39, "DEFAULT_BUSYDLG", 2, 1, "UI_NET_ACCOUNT_CREATED")); break;
            case 0 when result.Selector == 17:
                _ui.Add(new(0x36, "UI_WND_NETWORK", 1, 3)); OwnProfileRequestCount++; break;
            case 13: _ui.Add(new(0x36, "UI_WND_NETWORK", 3, 0)); break;
            case 3 or 5 or 16 or 17 or 18 or 21 or 22 or 35 or 0: break;
            case > 36:
                var text = string.Create(CultureInfo.InvariantCulture, $"Lobby Error {unchecked((int)result.ResultCode)}: {result.Text}!");
                _ui.Add(new(0x39, "DEFAULT_BUSYDLG", 2, text.Length > 150 ? 7u : 1u, Text: text)); break;
            default:
                // Native result 9 is suppressed by a nonzero controller+30; unknown baseline stays a conditional request.
                if (result.ResultCode == 9 && windowValue is null) { _conditionalResults.Add(result); break; }
                if (result.ResultCode == 9 && windowValue != 0) break;
                CloseBusy(); _ui.Add(new(0x39, "DEFAULT_BUSYDLG", 2, result.ResultCode == 1 ? 7u : 1u, GoldServiceErrorKeys.Get(result.ResultCode))); break;
        }
    }
    public string? Resolve(GoldLobbyControlNotice notice)
    {
        string? Resource(string? key) => key is null ? null : _resources.Strings.GetValueOrDefault(SacredResourceHash.Compute(key));
        var text = Resource(notice.ResourceKey); if (text is null) return null;
        var slot = notice.Event.Message.Sender.ToString(CultureInfo.InvariantCulture);
        if (notice.Event is GoldServiceErrorEvent error)
        {
            if (error.Parameter != 0) return string.Create(CultureInfo.InvariantCulture, $"{text} ({unchecked((int)error.Parameter)})");
            if (error.ErrorCode != 107) return text;
            if (!string.IsNullOrEmpty(notice.PlayerName)) return $"{text} {notice.PlayerName}\\r!";
            return Resource("UI_SPIELER") is { } prefix ? $"{text} {prefix} {slot}!" : null;
        }
        if (!string.IsNullOrEmpty(notice.PlayerName)) return $"{notice.PlayerName}\\r {text}.";
        return Resource("UI_SPIELER") is { } name ? $"{name} {slot} {text}." : null;
    }
    internal void Clear()
    {
        _conditionalResults.Clear(); _notices.Clear(); _ui.Clear(); _departures.Clear(); _channelQueries.Clear(); Roster.Clear(); Settings.Clear();
        ReceivedCount = SingletonListClearCount = Native117RequestCount = ClockResetRequestCount = ServiceSettingRequestCount = OwnProfileRequestCount = 0;
        NativeFlag3819 = NativeFlag381A = NativeFlag381B = NativeFlag381C = LocalDepartureClosesBusy = null;
    }
}
