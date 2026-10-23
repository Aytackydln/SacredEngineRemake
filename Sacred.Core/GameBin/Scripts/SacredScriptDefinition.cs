using System.Buffers.Binary;
using System.Text;
using Sacred.Core.World.Stairs;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>Immutable original 100-byte DefStru, shared by DefPos.bin and network updates.</summary>
public sealed class SacredScriptDefinition
{
    private readonly byte[] _bytes;
    private SacredScriptDefinition(byte[] bytes) => _bytes = bytes;
    public uint Kind => BinaryPrimitives.ReadUInt32LittleEndian(_bytes);
    public ReadOnlySpan<byte> NameStorage => _bytes.AsSpan(4, 64);
    public string Name
    {
        get
        {
            var end = NameStorage.IndexOf((byte)0);
            return Encoding.Latin1.GetString(end < 0 ? NameStorage : NameStorage[..end]);
        }
    }
    public int Parameter(int index)
    {
        if ((uint)index >= 8) throw new ArgumentOutOfRangeException(nameof(index));
        return BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(68 + index * 4));
    }
    public byte[] ToPayload() => _bytes.ToArray();
    public static SacredScriptDefinition FromPayload(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != SacredDefPosPositionLayout.SerializedSize)
            throw new InvalidDataException("Script definition requires 100 bytes.");
        return new(bytes.ToArray());
    }
}
