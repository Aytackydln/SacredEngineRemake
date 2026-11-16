namespace Sacred.GameplayHost.Networking;

/// <summary>One private, independently produced bank source. Null section means
/// an uncounted bank-1 global block. This is not a complete bank or upload.</summary>
public sealed record WorldHostBankSource(string Name, ushort Bank, int? Section,
    uint Count, ReadOnlyMemory<byte> Bytes, string? Blocker);
