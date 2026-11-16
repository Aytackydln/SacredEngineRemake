namespace Sacred.GameplayHost.Networking;

/// <summary>Pure storage for strings belonging to the captured controller.
/// Reads report explicit native length, including embedded NUL. Writes retain
/// owned contents; OS ANSI conversion and logging are separate service bodies.</summary>
public sealed record WorldSessionStringStorage(Func<uint, ReadOnlyMemory<byte>> ReadAnsi,
    Action<uint, ReadOnlyMemory<byte>> WriteAnsi, Func<uint, ReadOnlyMemory<char>> ReadWide,
    Action<uint, ReadOnlyMemory<char>> WriteWide);
