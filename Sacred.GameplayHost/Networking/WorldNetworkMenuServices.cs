namespace Sacred.GameplayHost.Networking;

/// <summary>Caller-owned menu/page storage and synchronous native services.
/// SetBits chooses 0x732550 (true) or 0x7325C0 (false). Visibility receives the
/// full native argument word, not just its low boolean byte. Multiplayer must
/// already be established; ResolveController can construct/reenter or replace it.</summary>
public sealed record WorldNetworkMenuServices(WorldControlMemory Menu,
    Func<byte> ReadMultiplayer, Func<WorldControlMemory> ResolveController,
    Func<uint, WorldControlMemory> ReadPage, Action<uint, uint, bool> SetBits,
    Action<uint, uint> SetVisibility, Action<uint, uint> UpdateList);
