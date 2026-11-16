namespace Sacred.GameplayHost.Networking;

/// <summary>Current storage/getters for accepted admission. Local/replacement
/// name and selected-address (ReadSessionWord / 0x7F5F60) getters are pure,
/// established inputs. Logging owns its
/// original formatting/time services; identifier and alternate identity may
/// reenter. Roster storage is the same captured controller used by the outer branch.</summary>
public sealed record WorldAdmissionStagingServices(WorldRosterLiveServices Roster,
    Func<WorldControlMemory?> ReadConnection, Func<ReadOnlyMemory<byte>> ReadLocalName,
    Func<ReadOnlyMemory<byte>> ReadReplacementName, Func<uint> ReadSessionWord,
    Func<int> ReadAlternateIdentity, Action<string> Log);
