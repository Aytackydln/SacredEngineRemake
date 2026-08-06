using System.Buffers.Binary;
using System.Text;

namespace Sacred.Core.World.Stairs;

/// <summary>A named world position from the first table in NetScript/DefPos.bin.</summary>
public readonly record struct SacredDefPosPosition(
    string Name,
    int X,
    int Y,
    int Z)
{
    private const int HeaderSize = sizeof(uint);
    private const int RecordSize = 100;
    private const int NameOffset = 4;
    private const int NameLength = 64;
    private const int XOffset = 68;
    private const int YOffset = 72;
    private const int ZOffset = 76;

    public static IReadOnlyList<SacredDefPosPosition> ReadMany(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException("DefPos.bin is too small to contain its position count.");

        var count = BinaryPrimitives.ReadUInt32LittleEndian(data);
        var tableLength = checked(HeaderSize + (long)count * RecordSize);
        if (tableLength > data.Length)
            throw new InvalidDataException("DefPos.bin ends inside its named-position table.");

        var positions = new SacredDefPosPosition[count];
        for (var index = 0; index < positions.Length; index++)
        {
            var record = data.Slice(HeaderSize + index * RecordSize, RecordSize);
            var nameBytes = record.Slice(NameOffset, NameLength);
            var terminator = nameBytes.IndexOf((byte)0);
            if (terminator >= 0)
                nameBytes = nameBytes[..terminator];

            positions[index] = new SacredDefPosPosition(
                Encoding.Latin1.GetString(nameBytes),
                BinaryPrimitives.ReadInt32LittleEndian(record.Slice(XOffset)),
                BinaryPrimitives.ReadInt32LittleEndian(record.Slice(YOffset)),
                BinaryPrimitives.ReadInt32LittleEndian(record.Slice(ZOffset)));
        }

        return positions;
    }
}
