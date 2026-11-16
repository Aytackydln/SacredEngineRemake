namespace Sacred.GameplayHost.Runtime;

public sealed record SacredCrtWriteServices(Func<ReadOnlyMemory<byte>, SacredCrtWriteResult> Write,
    Func<int, int, bool> Seek);
