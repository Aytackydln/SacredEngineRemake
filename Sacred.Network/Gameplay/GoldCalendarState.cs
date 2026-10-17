using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>Gold calendar export 0x417990/import 0x417A00; the last two bytes are padding.</summary>
public sealed class GoldCalendarState
{
    public const int Size = 16;
    private readonly byte[] _payload;
    private GoldCalendarState(byte[] payload) => _payload = payload;
    public int Year => BinaryPrimitives.ReadInt32LittleEndian(_payload);
    public ushort Month => Short(4);
    public ushort Day => Short(6);
    public ushort Hours => Short(8);
    public ushort Minutes => Short(10);
    public ushort Seconds => Short(12);
    public double DayFraction => (Hours * 3600.0 + Minutes * 60.0 + Seconds) / 86400.0;
    public bool HasValidTime => Hours < 24 && Minutes < 60 && Seconds < 60;
    public static GoldCalendarState FromPayload(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Size) throw new InvalidDataException("Gold calendar data requires sixteen bytes.");
        return new(bytes.ToArray());
    }
    public byte[] ToPayload() => _payload.ToArray();
    private ushort Short(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(_payload.AsSpan(offset));
}
