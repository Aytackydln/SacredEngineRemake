namespace Sacred.GameplayHost.Networking;

/// <summary>Original family-5 message type 0x10, command 0x38 at +4,
/// independently preserving +8 mode,
/// +C row, +10 style, +14 localized tooltip and the copied row-name string.</summary>
public sealed record WorldRosterUiRequest(string Target, uint Mode, byte Slot, uint Style,
    string? Name = null, string? Tooltip = null)
{
    public uint Family => 5;
    public uint MessageType => 0x10;
    public uint EventId => 0x38;
    public string Scope => "NETMGR";
}
