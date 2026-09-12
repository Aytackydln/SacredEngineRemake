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

    /// <summary>Native DefStru.type (eDefTypes).</summary>
    [FieldOffset(0x00)] public readonly WorldDefinitionKind Kind;

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

    /// <summary>Native DefStru.p[3]; meaning depends on the definition kind.</summary>
    [FieldOffset(0x50)] public readonly int Parameter3;
    /// <summary>Native DefStru.p[4]; meaning depends on the definition kind.</summary>
    [FieldOffset(0x54)] public readonly int Parameter4;
    /// <summary>Native DefStru.p[5]; meaning depends on the definition kind.</summary>
    [FieldOffset(0x58)] public readonly int Parameter5;
    /// <summary>Native DefStru.p[6]; meaning depends on the definition kind.</summary>
    [FieldOffset(0x5C)] public readonly int Parameter6;
    /// <summary>Native DefStru.p[7]; meaning depends on the definition kind.</summary>
    [FieldOffset(0x60)] public readonly int Parameter7;
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

    public static IReadOnlyList<SacredDefPosPosition> ReadMany(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException("DefPos.bin is too small to contain its position count.");

        var count = BinaryPrimitives.ReadUInt32LittleEndian(data);
        var headerSize = HeaderSize;
        if (count == SacredDefPosVersionedHeaderLayout.FormatMarker)
        {
            headerSize = SacredDefPosVersionedHeaderLayout.SerializedSize;
            if (data.Length < headerSize)
                throw new InvalidDataException("DefPos.bin ends inside its versioned header.");
            count = BinaryPrimitives.ReadUInt32LittleEndian(data[sizeof(uint)..]);
        }

        var tableLength = checked(headerSize + (long)count * RecordSize);
        if (tableLength > data.Length)
            throw new InvalidDataException("DefPos.bin ends inside its named-position table.");

        var positions = new List<SacredDefPosPosition>((int)count);
        for (var index = 0; index < count; index++)
        {
            var record = data.Slice(headerSize + index * RecordSize, RecordSize);
            var layout = MemoryMarshal.Read<SacredDefPosPositionLayout>(record);
            if (layout.Kind != WorldDefinitionKind.Position)
                continue;
            var nameBytes = record.Slice(NameOffset, NameLength);
            var terminator = nameBytes.IndexOf((byte)0);
            if (terminator >= 0)
                nameBytes = nameBytes[..terminator];

            positions.Add(new SacredDefPosPosition(
                Encoding.Latin1.GetString(nameBytes),
                layout.X, layout.Y, layout.Z));
        }

        return positions;
    }
}
