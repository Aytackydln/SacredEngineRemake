namespace Sacred.GameplayHost.Networking;

/// <summary>Original fopen/fgets/fclose boundaries. ReadLine returns delivered
/// bytes excluding fgets' appended NUL (embedded input NULs remain), or null at
/// EOF/error. The file service owns CRT text-mode translation and actual I/O.</summary>
public sealed record WorldSettingsFileServices(Func<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, uint> Open,
    Func<uint, int, ReadOnlyMemory<byte>?> ReadLine, Action<uint> Close);
