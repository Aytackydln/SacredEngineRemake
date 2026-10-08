using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.Pak.Items;

[InlineArray(ItemsPakEntryModelDescLayout.ModelNameLength)]
public struct ItemsPakEntryModelNameBytes
{
    private byte _element0;
}

[InlineArray(3)]
public struct ItemsPakEntryDimensions
{
    private byte _element0;
}

[InlineArray(2)]
public struct ItemsPakEntryBookEntries
{
    private byte _element0;
}

[InlineArray(10)]
public struct ItemsPakEntryReservedBytes
{
    private byte _element0;
}

/// <summary>
/// Known fixed fields in one 128-byte Items.pak model-description record. Symbol: cObjectShared
/// </summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct ItemsPakEntryModelDescLayout
{
    public const int SerializedSize = 128;
    internal const int Size = SerializedSize;
    internal const int ModelNameLength = 32;
    private const int ModelNameOffset = 55;

    /// <summary>Raw graphic representation used by Sacred.exe.</summary>
    [FieldOffset(0)]
    public readonly SacredItemGraphicType GraphicType;

    /// <summary>The native world-selection predicate, authored at offset 0x00, bit 8.</summary>
    public bool IsSelectable => (GraphicType & SacredItemGraphicType.Selectable) != 0;

    /// <summary>
    /// Rendering flags stored separately from <see cref="GraphicType"/>.
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

    /// <summary>Packed ARGB color (<c>cObjectShared::argb</c> in native metadata).</summary>
    [FieldOffset(12)]
    public readonly uint PackedColor;

    /// <summary>Base group identifier for an assembled Mixed.pak sprite.</summary>
    [FieldOffset(16)]
    public readonly uint MixedBaseGroupId;

    /// <summary>Authored blocking radius (<c>cObjectShared::blockRadius</c>).
    /// Gold 0x428CE0 reads this field for a valid item and substitutes 50 when
    /// the stored radius is zero. Generic FX subtype 6 uses that result to scale
    /// mesh-seeded particle size, gravity and size change (0x7972B0..0x797302).
    /// Demo 0x415EB0 substitutes 30 for zero; cObject3D::render passes the resolved
    /// value to renderShadowSimple as its quad half-extent (0x42D72E..0x42D766).
    /// This shadow size is independent of animated mesh bounds.</summary>
    [FieldOffset(20)]
    public readonly uint BlockRadius;

    /// <summary>Index into the loaded Weapon.pak table; synthesized again by the native loader.</summary>
    [FieldOffset(24)]
    public readonly ushort WeaponEntry;

    /// <summary>Runtime index into Creature.pak, resolved by Gold 0x43AD80. The native loader
    /// rebuilds it from template ItemType fields; the authored descriptor can contain zero.</summary>
    [FieldOffset(26)]
    public readonly ushort CreatureEntry;

    /// <summary>Book text resource identifier.</summary>
    [FieldOffset(28)]
    public readonly uint BookTextId;

    /// <summary>Object resource identifier (<c>cObjectShared::resourceid</c>).</summary>
    [FieldOffset(32)]
    public readonly uint ResourceId;

    /// <summary>
    /// sndProfiles.pak profile identifier. Sacred.exe uses this profile for authored object and
    /// weapon sounds; zero requests its equipment-type fallback for supported weapon families.
    /// </summary>
    [FieldOffset(36)]
    public readonly uint SoundProfileId;

    /// <summary>Horizontal model or sprite anchor.</summary>
    [FieldOffset(40)]
    public readonly short AnchorX;

    /// <summary>Vertical model or sprite anchor.</summary>
    [FieldOffset(42)]
    public readonly short AnchorY;

    /// <summary>Number of frames used by a static-world sprite animation.</summary>
    [FieldOffset(44)]
    public readonly ushort StaticSpriteFrameCount;

    /// <summary>Item family used by Sacred.exe gameplay, inventory and equipment attachment.
    /// Demo 0x401A10 / Gold 0x401AE0 use Ring (8) to choose the equipped slot's Finger31
    /// bone and Amulet (20) for the neck lookup. The slot decides rigid versus wearable binding.</summary>
    [FieldOffset(46)]
    // Native inventory slot predicates also read this family (Demo 0x4277A0,
    // Gold 0x43EFC0). It determines shield/armor/jewelry roles even when the
    // Weapon.pak type byte is zero; only Weapon family uses its weapon subtype.
    public readonly SacredItemCategory Category;

    /// <summary>
    /// Authored byte <c>cObjectShared::layer</c>. The Demo world queue builder does
    /// not read this field: it selects floor/shadow/floor2/object/ceiling queues
    /// from graphic flags and Static.pak instance state. Its value must not be
    /// treated as a render queue or a depth index without further evidence.
    /// </summary>
    [FieldOffset(47)]
    public readonly byte Layer;

    /// <summary>Static-sprite frame duration in 10-millisecond units.</summary>
    [FieldOffset(48)]
    public readonly byte StaticSpriteFrameDuration10Ms;

    /// <summary>
    /// Native <c>cObjectShared::gfxFileID</c>, an unsigned byte at offset 0x31.
    /// Its runtime selection behavior has not yet been verified.
    /// See docs/research/items-model-desc-continuation.md.
    /// </summary>
    [FieldOffset(49)]
    public readonly byte GfxFileId;

    /// <summary>
    /// Authored spatial extent. For animated light mini-objects this is the
    /// visible halo diameter. For invisible world-light marker entries it is
    /// the radial reach, so renderers using full quad size must double it.
    /// </summary>
    [FieldOffset(50)]
    public readonly ushort Radius;

    /// <summary>
    /// Three packed model extents named <c>lwh3d</c> by the native type. All three
    /// bytes are zero in every inspected Demo and Gold Items.pak record. Demo
    /// model visibility uses CABBCache/Granny bounds instead; these bytes do not
    /// establish a serialized bounds center. See docs/research/model-bounds-demo.md.
    /// </summary>
    [FieldOffset(52)]
    public readonly ItemsPakEntryDimensions Dimensions3D;

    /// <summary>Null-terminated model resource name encoded as ISO-8859-1.</summary>
    [FieldOffset(ModelNameOffset)]
    [BinaryString("ModelName", ModelNameLength, "ISO-8859-1")]
    public readonly ItemsPakEntryModelNameBytes ModelNameBytes;

    /// <summary>Authored model facing in projected-world degrees (<c>cObjectShared::angle3D</c>).</summary>
    [FieldOffset(87)]
    public readonly float Angle3D;

    /// <summary>
    /// Cell index in the 16x16 <c>SHADOW_TREE00.TGA</c> static-shadow atlas.
    /// </summary>
    [FieldOffset(91)]
    public readonly ushort StaticShadowAtlasCellIndex;

    /// <summary>Half-pixel horizontal coordinate of the shadow's ground anchor.</summary>
    [FieldOffset(93)]
    public readonly short StaticShadowAnchorX;

    /// <summary>Half-pixel vertical coordinate of the shadow's ground anchor.</summary>
    [FieldOffset(95)]
    public readonly short StaticShadowAnchorY;

    /// <summary>Two compact book-entry selectors.</summary>
    [FieldOffset(97)]
    public readonly ItemsPakEntryBookEntries BookEntries;

    /// <summary>Selects the contact or directionally projected shadow quad.</summary>
    [FieldOffset(99)]
    public readonly SacredItemStaticShadowProjection StaticShadowProjection;

    /// <summary>
    /// Authored half-width of the shadow footprint and, for directional shadows,
    /// the length of the fixed +X/-Y screen-space projection.
    /// </summary>
    [FieldOffset(100)]
    public readonly ushort StaticShadowContactExtent;

    /// <summary>
    /// Texture identifier used by an attached model effect.
    /// </summary>
    [FieldOffset(102)]
    public readonly uint EffectTextureId;

    /// <summary>Rune combat-art code. Demo getUpgradeSpellMove (0x424960) returns this
    /// word; consuming a rune looks it up in the spell and special-move definitions.</summary>
    [FieldOffset(106)]
    public readonly ushort UpgradeItemType;

    /// <summary>Upgrade behavior flags. Rune records use 1 for spells and 2 for special moves.</summary>
    [FieldOffset(108)]
    public readonly ushort UpgradeFlags;

    /// <summary>
    /// Compact model-effect selector returned by Sacred.exe 0x426520. Selector 9
    /// enables white <c>PARTICLE_GLOW03.TGA</c> billboards at <c>sera03_fx0..3</c>.
    /// </summary>
    [FieldOffset(110)]
    public readonly byte EffectTextureIndex;

    /// <summary>Gore rendering category.</summary>
    [FieldOffset(111)]
    public readonly byte GoreType;

    /// <summary>3D model resource identifier.</summary>
    [FieldOffset(112)]
    public readonly uint ModelId;

    /// <summary>Object switch behavior.</summary>
    [FieldOffset(116)]
    public readonly ushort SwitchType;

    [FieldOffset(118)]
    public readonly ItemsPakEntryReservedBytes Reserved;

    /// <summary>Compatibility name used by item loading code.</summary>
    public uint ItemId => ResourceId;

    public bool IsLightEmitting => GraphicFlags.HasFlag(SacredItemGraphicFlags.LightEmitting);
    public bool CastsStaticShadow => GraphicFlags.HasFlag(SacredItemGraphicFlags.CastsStaticShadow);

    /// <summary>Draws in the final world-object layer, above ordinary scenery and models.</summary>
    public bool IsFrontLayer => GraphicFlags.HasFlag(SacredItemGraphicFlags.FrontLayer);

    public bool SectorEmbeddable => GraphicType.HasFlag(SacredItemGraphicType.Shadowable) && !GraphicType.HasFlag(SacredItemGraphicType.OverWater);

    public bool AllowsTransparency => GraphicType.HasFlag(SacredItemGraphicType.AllowsTransparency);

    public bool UsesMiniObjectTexture => GraphicType.HasFlag(SacredItemGraphicType.MiniObject);

    public bool EmitsAnimatedSpriteHalo => UsesMiniObjectTexture && IsLightEmitting;

    public bool IsWorldLightMarker => GraphicType.HasFlag(SacredItemGraphicType.LightMarker) && IsLightEmitting;

    public bool MayContainMixedSpriteEmission => GraphicType.HasFlag(SacredItemGraphicType.MiniObject) && MixedBaseGroupId != 0;

    /// <summary>Copies a base visual while preserving the derived item's identity, as in
    /// Sacred.exe 0x425A60..0x425A92. The original archive record remains unchanged.</summary>
    public ItemsPakEntryModelDescLayout WithItemId(uint itemId)
    {
        var copy = this;
        var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref copy, 1));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.Slice(32, 4), itemId);
        return copy;
    }

    /// <summary>Returns one byte from the unmodified 0x80-byte game-file record.</summary>
    public byte GetRawByte(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(offset, SerializedSize);
        var descriptor = MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.AsRef(in this),
            1);
        return MemoryMarshal.AsBytes(descriptor)[offset];
    }
}
