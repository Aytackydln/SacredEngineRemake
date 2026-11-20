namespace Sacred.GameplayHost.Networking;

/// <summary>Initialization progress, separate from upload approval and scene readiness.</summary>
public enum WorldHostInitializationPhase
{
    Connecting, WaitingForHostAssignment, WaitingForReservations, ReservationsAvailable,
    PreparingCharacter, WaitingForActorBinding, ExecutingStartup, StartupBlocked, StartupComplete, Disconnected,
    WaitingForStartupInputs, CampaignBlocked, CampaignComplete, MinimalUploadApproved
}
