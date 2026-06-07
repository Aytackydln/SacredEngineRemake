using System;

namespace Sacred.Engine.Graphics;

public sealed class Dx12Shader
{
    private readonly Func<byte[]>[] _sourceReaders;

    public Dx12Shader(
        string name,
        EmbeddedResource_Shaders resource,
        string entryPoint,
        string shaderTarget)
        : this(name, [() => resource.ReadAllBytes()], entryPoint, shaderTarget)
    {
    }

    public Dx12Shader(
        string name,
        EmbeddedResource_ShadersHdr resource,
        string entryPoint,
        string shaderTarget,
        EmbeddedResource_ShadersHdr? header = null)
        : this(
            name,
            header.HasValue
                ? [() => header.Value.ReadAllBytes(), () => resource.ReadAllBytes()]
                : [() => resource.ReadAllBytes()],
            entryPoint,
            shaderTarget)
    {
    }

    private Dx12Shader(
        string name,
        Func<byte[]>[] sourceReaders,
        string entryPoint,
        string shaderTarget)
    {
        Name = name;
        _sourceReaders = sourceReaders;
        ShaderEntry = entryPoint;
        ShaderTarget = shaderTarget;
    }

    public string Name { get; }

    public string ShaderEntry { get; }

    public string ShaderTarget { get; }

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
}
