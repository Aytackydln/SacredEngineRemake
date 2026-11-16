namespace Sacred.GameplayHost.Networking;

public readonly record struct WorldHostedServerExitStatus(bool Success, uint Code);

/// <summary>Explicit hosted-process OS boundaries. No process/window operations
/// are performed by this library. PostClose denotes the native message 2 with
/// zero parameters; Terminate denotes exit code zero.</summary>
public sealed record WorldHostedServerProcessServices(Action<uint> PostClose, Func<string, uint> FindWindow,
    Action<uint> Terminate, Func<uint, WorldHostedServerExitStatus> ReadExitStatus, Action<uint> Sleep);
