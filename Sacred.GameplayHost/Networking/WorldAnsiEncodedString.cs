namespace Sacred.GameplayHost.Networking;

/// <summary>The original optional count output is the first API result, which
/// can differ from final C-string byte length and the second API result.</summary>
public sealed record WorldAnsiEncodedString(ReadOnlyMemory<byte> Value, int RequiredByteCount);
