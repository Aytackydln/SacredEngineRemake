namespace Sacred.Network.Gameplay;

/// <summary>Observed native startup milestones. Approval does not imply that a remake scene or actor is ready.</summary>
public sealed record GoldWorldStartupStatus(bool AssignedClientHost = false, bool HostWorldAvailable = false,
    bool ParticipantStartApproved = false, int SectorEntryCount = 0, GoldSectorEntry? LastSectorEntry = null)
{
    /// <summary>Origin from the server's empty 0x18 broadcast. Admission's available bit alone
    /// does not identify the host or approve this client's own upload.</summary>
    public byte AvailableHostSlot { get; init; }
}
