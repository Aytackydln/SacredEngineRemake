using Sacred.Network.Gameplay;

namespace Sacred.Network.Admission;

public enum GoldAdmissionPhase
{
    NotRequested,
    WaitingForResponse,
    SlotAssigned,
    Rejected
}

public enum GoldPasswordStatus
{
    NotRequested,
    WaitingForResponse,
    Accepted,
    Rejected
}

/// <summary>Slot assignment precedes password/character/world entry; it does not mean a playable session.</summary>
public sealed record GoldAdmissionStatus(GoldAdmissionPhase Phase, byte PlayerSlot = 0,
    byte Flags = 0, uint HostToken = 0, uint RejectionReason = 0, uint DataMismatchMask = 0,
    GoldPasswordStatus Password = GoldPasswordStatus.NotRequested)
{
    public GoldWorldStartupStatus WorldStartup { get; init; } = new();
    public bool? SessionLocked { get; init; }
    public uint? SessionGameFlags { get; init; }
    public uint? AnnouncedMaxPlayers { get; init; }
    public uint? MaxPlayers { get; init; }
    public bool? SessionPaused { get; init; }
    public byte? PausedBy { get; init; }
}
