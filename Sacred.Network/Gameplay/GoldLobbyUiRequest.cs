namespace Sacred.Network.Gameplay;

/// <summary>Original UI event operands. Menu/dialog execution remains a presentation consumer.</summary>
public sealed record GoldLobbyUiRequest(uint EventId, string Target, uint Mode, uint Action,
    string? ResourceKey = null, string? Text = null, uint Parameter = 0)
{
    /// <summary>Established raw +0x0C operand when it is independent of the
    /// target's logical Action (+0x10 for network-window requests).</summary>
    public uint? NativeWord0C { get; init; }
}
public sealed record GoldLobbyControlNotice(GoldLobbyControlEvent Event, string? PlayerName, bool IsChat,
    uint? DialogAction, string? ResourceKey, string? PrefixKey = null);
public sealed record GoldLocalDepartureWork(GoldPlayerSessionEvent Event, bool? ClosesBusyDialog);
