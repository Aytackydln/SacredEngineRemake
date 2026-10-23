using System.Buffers.Binary;
using System.Text;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>One original Vectoren.bin function record, including unused name bytes and the full deletion word.</summary>
public sealed class SacredScriptFunctionDefinition
{
    private readonly byte[] _bytes;
    private SacredScriptFunctionDefinition(byte[] bytes) => _bytes = bytes;
    public string Name
    {
        get
        {
            var name = _bytes.AsSpan(0, 64);
            var end = name.IndexOf((byte)0);
            return Encoding.Latin1.GetString(end < 0 ? name : name[..end]);
        }
    }
    public int Start => BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(64));
    public int ByteLength => BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(68));
    public int Quest => BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(72));
    public int Number => BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(76));
    public uint Deleted => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(80));
    public byte DeletedFlag => _bytes[80];
    public byte[] ToPayload() => _bytes.ToArray();
    public static SacredScriptFunctionDefinition FromPayload(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != SacredScriptFunctionLayout.SerializedSize)
            throw new InvalidDataException("Function definition requires 84 bytes.");
        return new(bytes.ToArray());
    }
}
