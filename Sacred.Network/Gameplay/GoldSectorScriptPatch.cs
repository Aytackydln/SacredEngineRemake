using System.Buffers.Binary;
using System.Text;

namespace Sacred.Network.Gameplay;

/// <summary>Bank 3 section 5 and 0x1D0: native 88-byte mouseover sector patch. Callback, resource, range and mode fields are mapped by native opcode 94;
/// all received bytes remain intact. Gold 0x493440 selects unsigned tile coordinates; registration
/// at 0x4AB160 compares the first three words and tests bytes 8..15 for front insertion.</summary>
public sealed class GoldSectorScriptPatch
{
    public const int Size = 88;
    private readonly byte[] _bytes;
    private GoldSectorScriptPatch(byte[] bytes) => _bytes = bytes;
    public ushort X => BinaryPrimitives.ReadUInt16LittleEndian(_bytes);
    public ushort Y => BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(2));
    public ushort Surface => BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(4));
    /// <summary>Opcode 94 default 16000; tag 0x66 changes this word to 1.</summary>
    public ushort RangeInput => BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(6));
    public ushort FirstX => BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(8));
    public ushort FirstY => BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(10));
    public ushort LastX => BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(12));
    public ushort LastY => BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(14));
    public uint TextResource => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(16));
    /// <summary>Opcode 94 default zero; tags 0x10 and 0x0F select 1 and 2.</summary>
    public int Mode => BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(20));
    public string Callback
    {
        get
        {
            var text = _bytes.AsSpan(24,64); var end = text.IndexOf((byte)0);
            return Encoding.Latin1.GetString(end < 0 ? text : text[..end]);
        }
    }
    public bool InsertAtFront => BinaryPrimitives.ReadUInt64LittleEndian(_bytes.AsSpan(8)) != 0;
    public byte[] ToPayload() => _bytes.ToArray();
    public static GoldSectorScriptPatch FromPayload(ReadOnlySpan<byte> bytes) => bytes.Length == Size
        ? new(bytes.ToArray()) : throw new InvalidDataException("Sector script patch requires 88 bytes.");
}
