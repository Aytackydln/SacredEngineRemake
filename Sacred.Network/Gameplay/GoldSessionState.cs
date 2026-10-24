namespace Sacred.Network.Gameplay;

/// <summary>Observed session controls. Unknown until a notification arrives; lock changes do not end or approve world entry.</summary>
public sealed class GoldSessionState
{
    public GoldTeamReplica Teams { get; } = new();
    public GoldChatReplica Chat { get; } = new();
    public GoldChannelChatReplica ChannelChat { get; } = new();
    public GoldClientHostRoleReplica ClientHostRole { get; } = new();
    public GoldLobbyReplica Lobby { get; } = new();
    public GoldSessionNoticeReplica Notices { get; } = new();
    public bool? CheatsEnabled { get; private set; }
    public long CheatNotificationCount { get; private set; }
    public bool? NativeReceiveControlEnabled { get; private set; }
    public long ReceiveControlNotificationCount { get; private set; }
    internal void ApplyControl(GoldGameEvent entry)
    {
        switch (entry)
        {
            case GoldSessionReceiveControlEvent control:
                NativeReceiveControlEnabled = GoldSessionReceiveControlCodec.Decode(control.Message).Enabled;
                ReceiveControlNotificationCount++;
                break;
            case GoldLobbyEvent lobby: Lobby.Apply(lobby); break;
            case GoldSessionNoticeEvent notice: Notices.Apply(notice); break;
            case GoldChannelChatEvent chat: ChannelChat.Apply(chat); break;
            case GoldPlayerRosterEvent roster: ChannelChat.ApplyRoster(roster); break;
            case GoldClientHostRoleEvent role: ClientHostRole.Apply(role); break;
            case GoldChatEvent chat: Chat.Apply(chat); break;
            case GoldCheatsEnabledEvent cheats:
                CheatsEnabled = GoldCheatsEnabledCodec.Decode(cheats.Message).Enabled; CheatNotificationCount++;
                break;
            case GoldSessionLockEvent session: Apply(session); break;
            case GoldSessionGameFlagsEvent flags: Apply(flags); break;
            case GoldSessionPlayerLimitEvent limit: Apply(limit); break;
            case GoldSessionPauseEvent pause:
                var notification = GoldSessionPauseCodec.Decode(pause.Message);
                IsPaused = notification.IsPaused; PausedBy = notification.PausedBy; PauseNotificationCount++;
                break;
            case GoldTeamEvent team: Teams.Apply(team); break;
            case GoldAdmissionEvent admission:
                var decoded = GoldGameEventDecoder.Decode(admission.Message) as GoldAdmissionEvent
                    ?? throw new InvalidDataException("Team identity requires a Gold admission response.");
                Teams.SetLocalPlayer(decoded.PlayerSlot);
                ClientHostRole.Seed(decoded); ChannelChat.EnterSession();
                break;
        }
    }
    public bool? IsLocked { get; private set; }
    public long LockNotificationCount { get; private set; }
    public uint? GameFlags { get; private set; }
    public long GameFlagsNotificationCount { get; private set; }
    public ushort? NativeControlWord { get; private set; }
    public uint? AnnouncedMaxPlayers { get; private set; }
    public uint? MaxPlayers { get; private set; }
    public long PlayerLimitNotificationCount { get; private set; }
    public bool? IsPaused { get; private set; }
    public byte? PausedBy { get; private set; }
    public long PauseNotificationCount { get; private set; }
    /// <summary>Filter simulation time only. Connection, loading and receipt callbacks keep running.</summary>
    public float SimulationSeconds(float seconds) => IsPaused == true ? 0 : seconds;
    internal void Apply(GoldSessionPlayerLimitEvent entry)
    {
        var announced = GoldSessionPlayerLimitCodec.Decode(entry.Message).AnnouncedMaxPlayers;
        AnnouncedMaxPlayers = announced;
        MaxPlayers = GoldSessionPlayerLimitCodec.Resolve(announced, GameFlags, MaxPlayers);
        PlayerLimitNotificationCount++;
    }
    internal void Apply(GoldSessionLockEvent entry)
    {
        IsLocked = GoldSessionLockCodec.Decode(entry.Message).IsLocked;
        if (GameFlags is { } flags) GameFlags = IsLocked == true ? flags | GoldSessionGameFlagsCodec.LockMask : flags & ~GoldSessionGameFlagsCodec.LockMask;
        LockNotificationCount++;
    }
    internal void Apply(GoldSessionGameFlagsEvent entry)
    {
        var flags = GoldSessionGameFlagsCodec.Decode(entry.Message).Flags;
        GameFlags = flags; IsLocked = (flags & GoldSessionGameFlagsCodec.LockMask) != 0;
        NativeControlWord = GoldSessionGameFlagsCodec.NativeControlWord(flags); GameFlagsNotificationCount++;
    }
    internal void Clear()
    {
        NativeReceiveControlEnabled = null; ReceiveControlNotificationCount = 0;
        Teams.Clear();
        Notices.Clear(); Lobby.Clear();
        ChannelChat.Clear(); ClientHostRole.Clear();
        Chat.Clear(); CheatsEnabled = null; CheatNotificationCount = 0;
        IsPaused = null; PausedBy = null; PauseNotificationCount = 0;
        IsLocked = null; LockNotificationCount = 0; GameFlags = null; GameFlagsNotificationCount = 0; NativeControlWord = null;
        AnnouncedMaxPlayers = null; MaxPlayers = null; PlayerLimitNotificationCount = 0;
    }
}
