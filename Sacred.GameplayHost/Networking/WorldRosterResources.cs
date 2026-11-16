namespace Sacred.GameplayHost.Networking;

/// <summary>Captured resource service for per-row tooltip lookup. Localization
/// may reenter and change fields used by subsequent style checks.</summary>
public sealed record WorldRosterResources(Func<string, string> Localize);
