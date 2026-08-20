using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Sacred.Core.Binary;

namespace Sacred.Core.World.Stairs;

/// <summary>Header of the first named-position table in <c>DefPos.bin</c>.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredDefPosHeaderLayout
{
    /// <summary>Serialized header size.</summary>
    public const int SerializedSize = sizeof(uint);

    /// <summary>Number of named-position records in the first table.</summary>
    [FieldOffset(0x00)]
    public readonly uint PositionCount;
}

/// <summary>Fixed-size record in the first named-position table in <c>DefPos.bin</c>.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredDefPosPositionLayout
{
    /// <summary>Serialized size of one named-position record.</summary>
    public const int SerializedSize = 100;

    /// <summary>Null-terminated position name encoded as ISO-8859-1.</summary>
    [FieldOffset(0x04)]
    [BinaryString("Name", 64, "ISO-8859-1")]
    private readonly byte _name;

    /// <summary>World X coordinate.</summary>
    [FieldOffset(0x44)] public readonly int X;
    /// <summary>World Y coordinate.</summary>
    [FieldOffset(0x48)] public readonly int Y;
    /// <summary>World Z coordinate or authored elevation value.</summary>
    [FieldOffset(0x4C)] public readonly int Z;
}

/// <summary>A named world position from the first table in NetScript/DefPos.bin.</summary>
public readonly record struct SacredDefPosPosition(
    string Name,
    int X,
    int Y,
    int Z)
{
    private const int HeaderSize = SacredDefPosHeaderLayout.SerializedSize;
    private const int RecordSize = SacredDefPosPositionLayout.SerializedSize;
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
