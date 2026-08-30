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

    /// <summary>Raw graphic representation used by Sacred.exe.</summary>
    [FieldOffset(0)]
    public readonly SacredItemGraphicType GraphicType;

    /// <summary>
    /// Rendering flags stored separately from <see cref="GraphicType"/>. Sacred.exe
    /// tests low bit 0x0001 here when building static-shadow render-list entries.
    /// </summary>
    [FieldOffset(2)]
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

    /// <summary>
    /// Cell index in the 16x16 <c>SHADOW_TREE00.TGA</c> static-shadow atlas.
    /// </summary>
    [FieldOffset(0x5B)]
    public readonly ushort StaticShadowAtlasCellIndex;

    /// <summary>Half-pixel horizontal coordinate of the shadow's ground anchor.</summary>
    [FieldOffset(0x5D)]
    public readonly short StaticShadowAnchorX;

    /// <summary>Half-pixel vertical coordinate of the shadow's ground anchor.</summary>
    [FieldOffset(0x5F)]
    public readonly short StaticShadowAnchorY;

    /// <summary>Selects the contact or directionally projected shadow quad.</summary>
    [FieldOffset(0x63)]
    public readonly SacredItemStaticShadowProjection StaticShadowProjection;

    /// <summary>
    /// Authored extent of the shadow's ground-contact footprint. This is not a
    /// directional shadow length; projected shadows derive that from object height.
    /// </summary>
    [FieldOffset(0x64)]
    public readonly ushort StaticShadowContactExtent;

    /// <summary>
    /// Texture identifier used by an attached model effect. This field is part
    /// of the descriptor's model/static-sprite union and overlaps static-shadow data.
    /// </summary>
    [FieldOffset(102)]
    public readonly uint EffectTextureId;

    public bool IsLightEmitting => GraphicFlags.HasFlag(SacredItemGraphicFlags.LightEmitting);
    public bool CastsStaticShadow => GraphicFlags.HasFlag(SacredItemGraphicFlags.CastsStaticShadow);

    private SacredItemGraphicType Representation => GraphicType & SacredItemGraphicType.RepresentationMask;

    public bool UsesAnimatedMiniObject => Representation == SacredItemGraphicType.AnimatedMiniObject;
    public bool UsesStaticMiniObject => Representation == SacredItemGraphicType.StaticMiniObject;
    public bool UsesMixedSpriteOrLightMarker => Representation == SacredItemGraphicType.MixedSpriteOrLightMarker;
    public bool IsPresent => DescriptorFlags.HasFlag(SacredItemDescriptorFlags.Present);

    public bool UsesMiniObjectTexture => UsesAnimatedMiniObject || UsesStaticMiniObject;

    public bool EmitsAnimatedSpriteHalo => UsesMiniObjectTexture && IsLightEmitting;

    public bool IsWorldLightMarker => UsesMixedSpriteOrLightMarker && IsLightEmitting;

    public bool MayContainMixedSpriteEmission => UsesMixedSpriteOrLightMarker && MixedBaseGroupId != 0 && IsPresent;
}
