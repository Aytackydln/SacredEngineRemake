namespace Sacred.GameplayHost.Networking;

/// <summary>Synchronous boundaries for the original roster consumer, with its
/// resource singleton present. Storage accessors are pure. Profile delivery owns
/// by-value message construction and immediate dispatch. JoinNotice owns resource
/// resolution, current-name formatting and chat delivery for the selected row.
/// No Engine listener, allocation policy or additional player registry is installed.</summary>
public sealed record WorldRosterLiveServices(WorldControlMemory Controller, WorldRosterStringStorage Strings,
    Func<string, string> Identifier, Func<bool> ReadMultiplayer, Action<WorldRosterUiRequest> Ui,
    Action<WorldRosterProfileRequest> Profile, Action PrepareModal,
    Func<WorldRosterResources> ResolveResources, Action Refresh, Action<byte> JoinNotice);
