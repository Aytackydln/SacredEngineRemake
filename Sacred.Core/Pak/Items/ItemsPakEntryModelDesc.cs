using System.Text;
using Sacred.Core.Utils;

namespace Sacred.Core.Pak.Items;

public readonly record struct ItemsPakEntryModelDesc(
    SacredPakLocation PakLocation, // location of the entry in the pak file, useful for debugging and lookup
    ushort SomeShort2, // 2 bytes at offset 9, purpose unknown
    uint GraphicRenderFlags, // 4 bytes at offset 0
    uint TextureId, // 4 bytes at offset 8; item-specific texture.pak descriptor index for shared item models
    uint MixedBaseGroupId, // 4 bytes at offset 16; base id into mixed.pak static sprite groups
    uint ItemId, // 4 bytes at offset 32
    byte RenderClass, // 1 byte at offset 46; affects static object draw ordering
    ushort ModelTransformFlags, // 2 bytes at offset 48
    ushort ModelExtent, // 2 bytes at offset 50; character rows contain values like 120..200
    string ModelName, // null-terminated string at 55, max length 32 bytes (including null terminator)
    float ModelRotationDegrees, // unaligned 4-byte float at offset 87; character rows commonly contain 0 or 90
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

        var graphicRenderFlags = BitConverter.ToUInt32(bytes[..4]);
        var rawBytes1 = bytes[4..8];
        var textureId = BitConverter.ToUInt32(bytes[8..12]);
        var rawBytes2 = bytes[12..16];
        var mixedBaseGroupId = BitConverter.ToUInt32(bytes[16..20]);
        var rawBytes3 = bytes[20..32];
        var itemId = BitConverter.ToUInt32(bytes[32..36]);
        var rawBytes4 = bytes[36..46];
        var renderClass = bytes[46];
        var rawBytes5 = bytes[47..48];
        var modelTransformFlags = BitConverter.ToUInt16(bytes[48..50]);
        var modelExtent = BitConverter.ToUInt16(bytes[50..52]);
        var rawBytes6 = bytes[52..55];
        var modelName = ReadLocationString(bytes[55..87]);
        var modelRotationDegrees = BitConverter.ToSingle(bytes[87..91]);
        var rawBytes7 = bytes[91..112];
        var someShort1 = BitConverter.ToUInt16(bytes[112..114]);
        var rawBytes8 = bytes[114..128];
        
        var unknownBytes = ByteArrayUtils.Combine(
            ByteArrayUtils.Combine(rawBytes1, rawBytes2, rawBytes3, rawBytes4),
            ByteArrayUtils.Combine(
                ByteArrayUtils.Combine(rawBytes5, rawBytes6, rawBytes7),
                rawBytes8));

        return new ItemsPakEntryModelDesc(
            PakLocation: pakLocation,
            SomeShort2: someShort2,
            GraphicRenderFlags: graphicRenderFlags,
            TextureId: textureId,
            MixedBaseGroupId: mixedBaseGroupId,
            ItemId: itemId,
            RenderClass: renderClass,
            ModelTransformFlags: modelTransformFlags,
            ModelExtent: modelExtent,
            ModelName: modelName,
            ModelRotationDegrees: modelRotationDegrees,
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
