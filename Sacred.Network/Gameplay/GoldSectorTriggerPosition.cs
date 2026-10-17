using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>Bank 1 section 2: aligned 16-byte native tile position used by 0x493440.
/// X/Y divide by 64 with truncation toward zero. Surface 1 marks both outdoor and
/// indoor cells; 0 marks only outdoor, and larger bytes mark only their indoor plane.</summary>
public sealed class GoldSectorTriggerPosition
{
    public const int Size = 16;
    private readonly byte[] _bytes;
    private GoldSectorTriggerPosition(byte[] bytes) => _bytes = bytes;
    public int X => BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(4));
    public int Y => BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(8));
    public byte Surface => _bytes[12];
    public byte[] ToPayload() => _bytes.ToArray();
    public static GoldSectorTriggerPosition FromPayload(ReadOnlySpan<byte> bytes) => bytes.Length == Size
        ? new(bytes.ToArray()) : throw new InvalidDataException("Sector trigger position requires 16 bytes.");
}
