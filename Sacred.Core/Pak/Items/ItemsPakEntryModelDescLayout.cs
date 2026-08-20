using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sacred.Core.Pak.Items;

[InlineArray(ItemsPakEntryModelDescLayout.ModelNameLength)]
internal struct ItemsPakEntryModelNameBytes
{
    private byte _element0;
}

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
internal readonly struct ItemsPakEntryModelDescLayout
{
    public const int Size = 128;
    public const int ModelNameLength = 32;

    [FieldOffset(0)]
    public readonly uint GraphicRenderFlags;

    /// <summary>
    /// Texture.pak descriptor index used by atlas-backed mini objects. Static.pak
    /// bytes 0x2e..0x32 select a region or animation from this texture.
    /// </summary>
    [FieldOffset(4)]
    public readonly uint MiniObjectTextureId;

    [FieldOffset(8)]
    public readonly uint TextureId;

    [FieldOffset(9)]
    public readonly ushort SomeShort2;

    [FieldOffset(16)]
    public readonly uint MixedBaseGroupId;

    [FieldOffset(32)]
    public readonly uint ItemId;

    [FieldOffset(44)]
    public readonly ushort StaticSpriteFrameCount;

    [FieldOffset(46)]
    public readonly byte RenderClass;

    [FieldOffset(48)]
    public readonly ushort ModelTransformFlags;

    [FieldOffset(50)]
    public readonly ushort ModelExtent;

    [FieldOffset(55)]
    public readonly ItemsPakEntryModelNameBytes ModelNameBytes;

    [FieldOffset(102)]
    public readonly uint EffectTextureId;

    [FieldOffset(112)]
    public readonly ushort Unknown112;
}
