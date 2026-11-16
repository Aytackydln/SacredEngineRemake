namespace Sacred.GameplayHost.Runtime;

public sealed record SacredCrtReadServices(Func<Memory<byte>, SacredCrtReadResult> Read,
    Func<int, int, bool> Seek);
