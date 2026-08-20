using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.Pak.Items;

[InlineArray(ItemsPakEntryModelDescLayout.ModelNameLength)]
public struct ItemsPakEntryModelNameBytes
{
    private byte _element0;
}

/// <summary>Known fixed fields in one 128-byte Items.pak model-description record.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct ItemsPakEntryModelDescLayout
{
    public const int Size = 128;
    public const int ModelNameLength = 32;
    public const int ModelNameOffset = 55;

    /// <summary>Rendering flags whose low bits select the item's rendering class.</summary>
    [FieldOffset(0)]
    public readonly uint GraphicRenderFlags;

    /// <summary>
    /// Texture.pak descriptor index used by atlas-backed mini objects. Static.pak
    /// bytes 0x2e..0x32 select a region or animation from this texture.
    /// </summary>
    [FieldOffset(4)]
    public readonly uint MiniObjectTextureId;

    /// <summary>General Texture.pak identifier used by model-backed items.</summary>
    [FieldOffset(8)]
    public readonly uint TextureId;

    /// <summary>Unresolved two-byte value overlapping the texture-id bytes at offset 0x09.</summary>
    [FieldOffset(9)]
    [BinaryUnknown]
    public readonly ushort SomeShort2;

    /// <summary>Base group identifier for an assembled Mixed.pak sprite.</summary>
    [FieldOffset(16)]
    public readonly uint MixedBaseGroupId;

    /// <summary>Item identifier repeated within the model description.</summary>
    [FieldOffset(32)]
    public readonly uint ItemId;

    /// <summary>Number of frames used by a static-world sprite animation.</summary>
    [FieldOffset(44)]
    public readonly ushort StaticSpriteFrameCount;

    /// <summary>Low-level renderer category for this model description.</summary>
    [FieldOffset(46)]
    public readonly byte RenderClass;

    /// <summary>Flags controlling the model or sprite transform.</summary>
    [FieldOffset(48)]
    public readonly ushort ModelTransformFlags;

    /// <summary>
    /// Authored spatial extent. For invisible world-light marker entries this
    /// is the radial reach, so renderers using full quad size must double it.
    /// </summary>
    [FieldOffset(50)]
    public readonly ushort ModelExtent;

    /// <summary>Null-terminated model resource name encoded as ISO-8859-1.</summary>
    [FieldOffset(ModelNameOffset)]
    [BinaryString("ModelName", ModelNameLength, "ISO-8859-1")]
    public readonly ItemsPakEntryModelNameBytes ModelNameBytes;

    /// <summary>Texture identifier used by an attached visual effect.</summary>
    [FieldOffset(102)]
    public readonly uint EffectTextureId;

    /// <summary>Unresolved two-byte value at byte offset 0x70.</summary>
    [FieldOffset(112)]
    public readonly ushort Unknown112;

    public uint LowRenderClass => GraphicRenderFlags & ItemsPakEntry.LowRenderClassMask;
    public bool IsLightEmitting => (GraphicRenderFlags & ItemsPakEntry.LightEmittingGraphicFlag) != 0;
    public bool HasExtendedMixedSpriteGraphicFlag =>
        (GraphicRenderFlags & ItemsPakEntry.ExtendedMixedSpriteGraphicFlag) != 0;
    public bool UsesAnimatedMiniObjectRenderClass => LowRenderClass == ItemsPakEntry.AnimatedMiniObjectRenderClass;
    public bool UsesStaticMiniObjectRenderClass => LowRenderClass == ItemsPakEntry.StaticMiniObjectRenderClass;
    public bool UsesMixedSpriteOrLightMarkerRenderClass => LowRenderClass == ItemsPakEntry.MixedSpriteOrLightMarkerRenderClass;

    public bool UsesMiniObjectTexture =>
        MiniObjectTextureId != 0 && MixedBaseGroupId == 0 && TextureId == 0 && EffectTextureId == 0 &&
        (UsesAnimatedMiniObjectRenderClass || UsesStaticMiniObjectRenderClass);

    public bool EmitsAnimatedSpriteHalo =>
        UsesMiniObjectTexture && UsesAnimatedMiniObjectRenderClass && IsLightEmitting && ModelExtent > 0;

    public bool IsWorldLightMarker =>
        UsesMixedSpriteOrLightMarkerRenderClass && IsLightEmitting && ModelExtent > 0 &&
        MiniObjectTextureId == 0 && MixedBaseGroupId == 0 && TextureId == 0 && EffectTextureId == 0;

    public bool MayContainMixedSpriteEmission =>
        UsesMixedSpriteOrLightMarkerRenderClass && MixedBaseGroupId != 0 &&
        MiniObjectTextureId == 0 && TextureId == 0 && EffectTextureId == 0 &&
        StaticSpriteFrameCount == 0 && ModelTransformFlags == 0x0100 && ModelExtent == 0;
}
