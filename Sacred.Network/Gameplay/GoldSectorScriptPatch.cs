using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>Bank 3 section 5: native 88-byte pending sector patch. Unknown callback/name
/// bytes remain intact. Gold 0x493440 selects unsigned tile coordinates; registration
/// at 0x4AB160 compares the first three words and tests bytes 8..15 for front insertion.</summary>
public sealed class GoldSectorScriptPatch
{
    public const int Size = 88;
    private readonly byte[] _bytes;
    private GoldSectorScriptPatch(byte[] bytes) => _bytes = bytes;
    public ushort X => BinaryPrimitives.ReadUInt16LittleEndian(_bytes);
    public ushort Y => BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(2));
    public ushort Surface => BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(4));
    public bool InsertAtFront => BinaryPrimitives.ReadUInt64LittleEndian(_bytes.AsSpan(8)) != 0;
    public byte[] ToPayload() => _bytes.ToArray();
    public static GoldSectorScriptPatch FromPayload(ReadOnlySpan<byte> bytes) => bytes.Length == Size
        ? new(bytes.ToArray()) : throw new InvalidDataException("Sector script patch requires 88 bytes.");
}
