namespace Sacred.Core.Runtime;

public sealed class SacredSettingsDocument
{
    public ReadOnlyMemory<byte> Path { get; private set; }
    public SacredSettingsValues Values { get; } = new();
    public void SetPath(ReadOnlyMemory<byte> path) => Path = path.ToArray();
}
