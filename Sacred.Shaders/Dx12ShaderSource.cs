using System.Text;

namespace Sacred.Shaders;

public sealed class Dx12ShaderSource
{
    private readonly Func<byte[]>[] _sourceReaders;

    internal Dx12ShaderSource(
        string name,
        Func<byte[]>[] sourceReaders,
        string entryPoint,
        string shaderTarget)
    {
        Name = name;
        _sourceReaders = sourceReaders;
        EntryPoint = entryPoint;
        Target = shaderTarget;
    }

    public string Name { get; }

    public string EntryPoint { get; }

    public string Target { get; }

    public byte[] ReadAllBytes()
    {
        if (_sourceReaders.Length == 1)
            return _sourceReaders[0]();

        var sources = new byte[_sourceReaders.Length][];
        var length = 0;
        for (var i = 0; i < sources.Length; i++)
        {
            sources[i] = _sourceReaders[i]();
            length += sources[i].Length + 1;
        }

        var combined = new byte[length];
        var offset = 0;
        foreach (var source in sources)
        {
            source.CopyTo(combined.AsSpan(offset));
            offset += source.Length;
            combined[offset++] = (byte)'\n';
        }

        return combined;
    }

    public string ReadAllText() => Encoding.UTF8.GetString(ReadAllBytes());
}
