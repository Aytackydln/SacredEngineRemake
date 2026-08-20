using System.Runtime.InteropServices;
using System.Text;

namespace Sacred.Core.Pak.Items;

public readonly record struct ItemsPakEntry(
    ItemsPakEntryInfo EntryInfo,
    ItemsPakEntryModelDescLayout ModelDesc,
    string ModelName
)
{
    public const uint LightEmittingGraphicFlag = 0x00020000;
    public const uint ExtendedMixedSpriteGraphicFlag = 0x00010000;
    public const uint LowRenderClassMask = 0x0000000F;
    public const uint AnimatedMiniObjectRenderClass = 0x00000008;
    public const uint MixedSpriteOrLightMarkerRenderClass = 0x00000009;
    public const uint StaticMiniObjectRenderClass = 0x0000000C;

    private static readonly Encoding SacredEncoding = Encoding.GetEncoding("iso-8859-1");

    public ushort ItemIndex => EntryInfo.ItemIndex;
    public uint MiniObjectTextureId => ModelDesc.MiniObjectTextureId;
    public uint MixedBaseGroupId => ModelDesc.MixedBaseGroupId;
    public uint GraphicRenderFlags => ModelDesc.GraphicRenderFlags;
    public byte RenderClass => ModelDesc.RenderClass;
    public uint ItemId => ModelDesc.ItemId;
    public uint EffectTextureId => ModelDesc.EffectTextureId;
    public ushort StaticSpriteFrameCount => ModelDesc.StaticSpriteFrameCount;
    public byte StaticSpriteFrameDuration10Ms => (byte)ModelDesc.ModelTransformFlags;

    public static IEnumerable<ItemsPakEntry> ReadMany(
        byte[] pakBytes,
        IReadOnlyList<ItemsPakEntryInfo> entryInfos)
    {
        if (entryInfos.Count == 0)
            yield break;

        var firstOffset = entryInfos.Min(static entryInfo => entryInfo.ModelDescOffset);
        var lastOffset = entryInfos.Max(static entryInfo => entryInfo.ModelDescOffset);
        var byteLength = checked((int)(lastOffset - firstOffset) + ItemsPakEntryModelDescLayout.Size);
        if (firstOffset > int.MaxValue || firstOffset + byteLength > pakBytes.Length)
            throw new InvalidDataException("Items pak model descriptor table is outside the file bounds.");

        var layouts = MemoryMarshal.Cast<byte, ItemsPakEntryModelDescLayout>(
            pakBytes.AsSpan((int)firstOffset, byteLength)).ToArray();

        foreach (var entryInfo in entryInfos)
        {
            var offsetDelta = entryInfo.ModelDescOffset - firstOffset;
            if (offsetDelta % ItemsPakEntryModelDescLayout.Size != 0)
                throw new InvalidDataException($"Items pak model descriptor offset {entryInfo.ModelDescOffset} is not aligned to {ItemsPakEntryModelDescLayout.Size} bytes.");

            var layoutIndex = checked((int)(offsetDelta / ItemsPakEntryModelDescLayout.Size));
            if ((uint)layoutIndex >= (uint)layouts.Length)
                throw new InvalidDataException($"Items pak model descriptor offset {entryInfo.ModelDescOffset} is outside the marshalled descriptor table.");

            var layout = layouts[layoutIndex];
            yield return new ItemsPakEntry(entryInfo, layout, ReadModelName(layout.ModelNameBytes));
        }
    }

    private static string ReadModelName(ReadOnlySpan<byte> stringBytes)
    {
        var nullIndex = stringBytes.IndexOf((byte)0);
        return SacredEncoding.GetString(nullIndex < 0 ? stringBytes : stringBytes[..nullIndex]);
    }
}
