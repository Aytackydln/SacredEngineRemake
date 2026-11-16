namespace Sacred.GameplayHost.Networking;

/// <summary>Pure access to the captured controller's native row-name and
/// identifier arrays (+0x384C/+0x3918, 12-byte string records indexed by slot).
/// Their backing/lifetime belongs to the caller.</summary>
public sealed record WorldRosterStringStorage(Func<byte, string> ReadName,
    Action<byte, string> WriteName, Action<byte, string> WriteIdentifier);
