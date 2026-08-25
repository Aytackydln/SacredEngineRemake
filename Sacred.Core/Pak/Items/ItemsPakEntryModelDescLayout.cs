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
    internal const int Size = 128;
    internal const int ModelNameLength = 32;
    private const int ModelNameOffset = 55;

    /// <summary>
    /// Packed graphic type and rendering flags. The low nibble is a
    /// <see cref="SacredItemGraphicType"/>; the remaining bits are
    /// <see cref="SacredItemGraphicFlags"/> values.
    /// </summary>
    [FieldOffset(0)]
    public readonly SacredItemGraphicFlags GraphicFlags;

    /// <summary>
    /// Texture.pak descriptor index used by atlas-backed mini objects. Static.pak
    /// bytes 0x2e..0x32 select a region or animation from this texture.
    /// </summary>
    [FieldOffset(4)]
    public readonly uint MiniObjectTextureId;

    /// <summary>General Texture.pak identifier used by model-backed items.</summary>
    [FieldOffset(8)]
    public readonly uint TextureId;

    /// <summary>Base group identifier for an assembled Mixed.pak sprite.</summary>
    [FieldOffset(16)]
    public readonly uint MixedBaseGroupId;

    /// <summary>Item identifier repeated within the model description.</summary>
    [FieldOffset(32)]
    public readonly uint ItemId;

    /// <summary>
    /// sndProfiles.pak profile identifier. Sacred.exe uses this profile for authored object and
    /// weapon sounds; zero requests its equipment-type fallback for supported weapon families.
    /// </summary>
    [FieldOffset(36)]
    public readonly uint SoundProfileId;

    /// <summary>Number of frames used by a static-world sprite animation.</summary>
    [FieldOffset(44)]
    public readonly ushort StaticSpriteFrameCount;

    /// <summary>Item family used by Sacred.exe gameplay and inventory UI code.</summary>
    [FieldOffset(46)]
    public readonly SacredItemCategory Category;

    /// <summary>Static-sprite frame duration in 10-millisecond units.</summary>
    [FieldOffset(48)]
    public readonly byte StaticSpriteFrameDuration10Ms;

    /// <summary>Descriptor-state flags; separate from the animation duration byte.</summary>
    [FieldOffset(49)]
    public readonly SacredItemDescriptorFlags DescriptorFlags;

    /// <summary>
    /// Authored spatial extent. For animated light mini-objects this is the
    /// visible halo diameter. For invisible world-light marker entries it is
    /// the radial reach, so renderers using full quad size must double it.
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

    public SacredItemGraphicType GraphicType =>
        (SacredItemGraphicType)((uint)GraphicFlags & 0x0F);

    public bool IsLightEmitting => (GraphicFlags & SacredItemGraphicFlags.LightEmitting) != 0;
    public bool UsesExtendedMixedSprite =>
        (GraphicFlags & SacredItemGraphicFlags.ExtendedMixedSprite) != 0;
    public bool UsesAnimatedMiniObject => GraphicType == SacredItemGraphicType.AnimatedMiniObject;
    public bool UsesStaticMiniObject => GraphicType == SacredItemGraphicType.StaticMiniObject;
    public bool UsesMixedSpriteOrLightMarker => GraphicType == SacredItemGraphicType.MixedSpriteOrLightMarker;
    public bool IsPresent => (DescriptorFlags & SacredItemDescriptorFlags.Present) != 0;

    public bool UsesMiniObjectTexture =>
        MiniObjectTextureId != 0 && MixedBaseGroupId == 0 && TextureId == 0 && EffectTextureId == 0 &&
        (UsesAnimatedMiniObject || UsesStaticMiniObject);

    public bool EmitsAnimatedSpriteHalo =>
        UsesMiniObjectTexture && UsesAnimatedMiniObject && IsLightEmitting && ModelExtent > 0;

    public bool IsWorldLightMarker =>
        UsesMixedSpriteOrLightMarker && IsLightEmitting && ModelExtent > 0 &&
        MiniObjectTextureId == 0 && MixedBaseGroupId == 0 && TextureId == 0 && EffectTextureId == 0;

    public bool MayContainMixedSpriteEmission =>
        UsesMixedSpriteOrLightMarker && MixedBaseGroupId != 0 &&
        MiniObjectTextureId == 0 && TextureId == 0 && EffectTextureId == 0 &&
        StaticSpriteFrameCount == 0 && IsPresent && ModelExtent == 0;
}
