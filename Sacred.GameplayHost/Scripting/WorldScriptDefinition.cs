using System.Buffers.Binary;
using System.Text;
using Sacred.Core.World.Stairs;

namespace Sacred.GameplayHost.Scripting;

/// <summary>One original DefStru record, retaining fields not changed by a script mutation.</summary>
public sealed class WorldScriptDefinition
{
    private readonly byte[] _bytes;
    private WorldScriptDefinition(byte[] bytes) => _bytes = bytes;
    public WorldDefinitionKind Kind => (WorldDefinitionKind)BinaryPrimitives.ReadInt32LittleEndian(_bytes);
    public string Name
    {
        get
        {
            var bytes = _bytes.AsSpan(4, 64);
            var end = bytes.IndexOf((byte)0);
            return Encoding.Latin1.GetString(end < 0 ? bytes : bytes[..end]);
        }
    }
    public int Parameter(int index)
    {
        if ((uint)index >= 8) throw new ArgumentOutOfRangeException(nameof(index));
        return BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(68 + index * 4));
    }
    public byte[] ToPayload() => _bytes.ToArray();
    public static WorldScriptDefinition FromPayload(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != SacredDefPosPositionLayout.SerializedSize) throw new InvalidDataException("Definition requires 100 bytes.");
        return new(bytes.ToArray());
    }
    public static WorldScriptDefinition Create(WorldDefinitionKind kind, string name, ReadOnlySpan<int> parameters)
    {
        if (name.Length >= 64 || name.Contains('\0') || name.Any(character => character > byte.MaxValue))
            throw new ArgumentException("Definition name must fit the native 64-byte storage.", nameof(name));
        if (parameters.Length > 8) throw new ArgumentOutOfRangeException(nameof(parameters));
        var bytes = new byte[SacredDefPosPositionLayout.SerializedSize];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, (int)kind);
        Encoding.Latin1.GetBytes(name).CopyTo(bytes, 4);
        for (var i = 0; i < parameters.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(68 + i * 4), parameters[i]);
        return new(bytes);
    }
}
