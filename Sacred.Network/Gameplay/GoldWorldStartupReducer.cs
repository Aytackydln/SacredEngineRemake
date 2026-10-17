namespace Sacred.Network.Gameplay;

/// <summary>Pure changes committed alongside admission only after the complete batch validates.</summary>
internal static class GoldWorldStartupReducer
{
    public static GoldWorldStartupStatus Apply(GoldWorldStartupStatus status, GoldGameEvent entry) => entry switch
    {
        GoldAdmissionEvent admission => new(
            AssignedClientHost: (admission.Flags & GoldWorldEntryCodec.AssignedHostFlag) != 0,
            HostWorldAvailable: (admission.Flags & GoldWorldEntryCodec.HostWorldAvailableFlag) != 0),
        GoldHostWorldAvailableEvent available => status with { HostWorldAvailable = true, AvailableHostSlot = available.Message.Origin },
        GoldParticipantStartApprovedEvent => status with { ParticipantStartApproved = true },
        GoldSectorEntryEvent sectors => status with { SectorEntryCount = status.SectorEntryCount + 1, LastSectorEntry = sectors.Entry },
        _ => status
    };
}
