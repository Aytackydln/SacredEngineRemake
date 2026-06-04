using System.Text;
using SacredItemSimulator.GamePak;
using SacredItemSimulator.Utils;

namespace Sacred.Core.Items;

public readonly record struct ItemsPakEntryModelDesc(
    SacredPakLocation PakLocation, // location of the entry in the pak file, useful for debugging and lookup
    ushort SomeShort2, // 2 bytes at offset 9, purpose unknown
    uint Int1, // 4 bytes at offset 0
    uint ItemId, // 4 bytes at offset 32
    string ModelName, // null-terminated string at 55, max length 34 bytes (including null terminator)
    ushort SomeShort1, // 2 bytes at offset 112, purpose unknown

    byte[] UnknownBytes
)
{
    private const int TotalSize = 128;

    private static readonly Encoding SacredEncoding = Encoding.GetEncoding("iso-8859-1");

    public static ItemsPakEntryModelDesc FromBytes(SacredPakFile pakFile, uint pakOffset, BinaryReader br)
    {
        var pakLocation = new SacredPakLocation(pakFile, pakOffset, TotalSize);
        br.BaseStream.Seek(pakOffset, SeekOrigin.Begin);

        var bytes = br.ReadBytes(TotalSize).AsSpan();

        var someShort2 = BitConverter.ToUInt16(bytes[9..11]);

        var int1 = BitConverter.ToUInt32(bytes[..4]);
        var rawBytes1 = bytes[4..32];
        var itemId = BitConverter.ToUInt32(bytes[32..36]);
        var rawBytes2 = bytes[36..55];
        var modelName = ReadLocationString(bytes[55..89]);
        var rawBytes3 = bytes[89..112];
        var someShort1 = BitConverter.ToUInt16(bytes[112..114]);
        var rawBytes4 = bytes[114..128];
        
        var unknownBytes = ByteArrayUtils.Combine(rawBytes1, rawBytes2, rawBytes3, rawBytes4);

        return new ItemsPakEntryModelDesc(
            PakLocation: pakLocation,
            SomeShort2: someShort2,
            Int1: int1,
            ItemId: itemId,
            ModelName: modelName,
            SomeShort1: someShort1,
            UnknownBytes: unknownBytes
        );
    }

    private static string ReadLocationString(Span<byte> stringBytes)
    {
        var nullIndex = stringBytes.IndexOf((byte)0);

        return SacredEncoding.GetString(stringBytes[..nullIndex]);
    }
}