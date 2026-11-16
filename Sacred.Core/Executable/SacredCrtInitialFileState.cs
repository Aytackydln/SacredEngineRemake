namespace Sacred.Core.Executable;

/// <summary>Original executable loader-image values. They initialize a private
/// host CRT model; they do not claim to observe another process's current globals.</summary>
public sealed record SacredCrtInitialFileState(uint CommitFlags, uint FileMode, uint Umask);
