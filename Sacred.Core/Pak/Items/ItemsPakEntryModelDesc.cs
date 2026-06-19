using System.Runtime.InteropServices;
using System.Text;

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
    string ModelName, // unaligned 4-byte float at offset 87; character rows commonly contain 0 or 90
    uint EffectTextureId // 4 bytes at offset 102; texture.pak descriptor index for model effect/fill/animated texture
)
{
    private static readonly Encoding SacredEncoding = Encoding.GetEncoding("iso-8859-1");

    public static IEnumerable<ItemsPakEntryModelDesc> ReadMany(
        SacredPakFile pakFile,
        byte[] pakBytes,
        IReadOnlyList<ItemsPakEntryInfo> entryInfos
    )
    {
        if (entryInfos.Count == 0)
            yield break;

        var firstOffset = entryInfos.Min(static entryInfo => entryInfo.ModelDescOffset);
        var layouts = ReadLayouts(pakBytes, entryInfos, firstOffset);

        foreach (var entryInfo in entryInfos)
        {
            var offsetDelta = entryInfo.ModelDescOffset - firstOffset;
            if (offsetDelta % ItemsPakEntryModelDescLayout.Size != 0)
                throw new InvalidDataException($"Items pak model descriptor offset {entryInfo.ModelDescOffset} is not aligned to {ItemsPakEntryModelDescLayout.Size} bytes.");

            var layoutIndex = checked((int)(offsetDelta / ItemsPakEntryModelDescLayout.Size));
            if ((uint)layoutIndex >= (uint)layouts.Length)
                throw new InvalidDataException($"Items pak model descriptor offset {entryInfo.ModelDescOffset} is outside the marshalled descriptor table.");

            yield return FromLayout(pakFile, entryInfo.ModelDescOffset, layouts[layoutIndex]);
        }
    }

    private static ItemsPakEntryModelDescLayout[] ReadLayouts(
        byte[] pakBytes,
        IReadOnlyList<ItemsPakEntryInfo> entryInfos,
        uint firstOffset
    )
    {
        var lastOffset = entryInfos.Max(static entryInfo => entryInfo.ModelDescOffset);
        var descriptorByteLength = checked((int)(lastOffset - firstOffset) + ItemsPakEntryModelDescLayout.Size);

        if (firstOffset > int.MaxValue || firstOffset + descriptorByteLength > pakBytes.Length)
            throw new InvalidDataException("Items pak model descriptor table is outside the file bounds.");

        return MemoryMarshal
            .Cast<byte, ItemsPakEntryModelDescLayout>(pakBytes.AsSpan((int)firstOffset, descriptorByteLength))
            .ToArray();
    }

    private static ItemsPakEntryModelDesc FromLayout(
        SacredPakFile pakFile,
        uint pakOffset,
        ItemsPakEntryModelDescLayout layout
    )
    {
        var pakLocation = new SacredPakLocation(pakFile, pakOffset, ItemsPakEntryModelDescLayout.Size);
        ReadOnlySpan<byte> modelNameBytes = layout.ModelNameBytes;

        return new ItemsPakEntryModelDesc(
            PakLocation: pakLocation,
            SomeShort2: layout.SomeShort2,
            GraphicRenderFlags: layout.GraphicRenderFlags,
            TextureId: layout.TextureId,
            MixedBaseGroupId: layout.MixedBaseGroupId,
            ItemId: layout.ItemId,
            RenderClass: layout.RenderClass,
            ModelTransformFlags: layout.ModelTransformFlags,
            ModelExtent: layout.ModelExtent,
            ModelName: ReadLocationString(modelNameBytes),
            EffectTextureId: layout.EffectTextureId
        );
    }

    private static string ReadLocationString(ReadOnlySpan<byte> stringBytes)
    {
        var nullIndex = stringBytes.IndexOf((byte)0);
        if (nullIndex < 0)
            nullIndex = stringBytes.Length;

        return SacredEncoding.GetString(stringBytes[..nullIndex]);
    }
}
