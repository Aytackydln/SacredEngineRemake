namespace Sacred.Core.Pak.Items;

/// <summary>
/// Rendering flags stored at Items.pak model-descriptor offset 0x02.
/// </summary>
[Flags]
public enum SacredItemGraphicFlags : ushort
{
    None = 0,

    /// <summary>
    /// Adds the object to Sacred.exe's static-shadow render path. The native
    /// render-list builder tests this bit before emitting its shadow entry.
    /// </summary>
    CastsStaticShadow = 0x0001,

    /// <summary>Marks a graphic that carries the existing halo/light-marker effect.</summary>
    LightEmitting = 0x0002,

    /// <summary>Observed unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte0004 = 0x0004,

    /// <summary>Observed unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte0008 = 0x0008,

    /// <summary>Scrolls an equipment multitexture fill.</summary>
    MultitextureScroll = 0x0010,

    /// <summary>Scrolls an effect texture vertically.</summary>
    VerticalTextureScroll = 0x0020,

    /// <summary>Observed unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte0040 = 0x0040,

    /// <summary>Places the graphic in a front render layer.</summary>
    FrontLayer = 0x0080,

    /// <summary>Observed unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte0100 = 0x0100,

    /// <summary>Observed unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte0200 = 0x0200,

    /// <summary>Observed unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte0400 = 0x0400,

    /// <summary>Observed unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte0800 = 0x0800,
}

/// <summary>
/// Static-world shadow shape stored at Items.pak model-descriptor offset 0x63.
/// </summary>
public enum SacredItemStaticShadowProjection : byte
{
    /// <summary>Maps the atlas mask to a centered ground-contact quad.</summary>
    Contact = 0,

    /// <summary>Maps the atlas mask to a quad projected away from the sun.</summary>
    Directional = 1,
}

/// <summary>
/// Graphic representation stored at Items.pak model-descriptor offset 0x00.
/// The low nibble selects the representation. Bit 0x10 allows a static world
/// object to fade when it obscures the player.
/// </summary>
[Flags]
public enum SacredItemGraphicType : ushort
{
    None = 0,
    Model = 0b0010,
    AnimatedMiniObject = 0b1000,
    MixedSpriteOrLightMarker = 0b1001,
    StaticMiniObject = 0b1100,
    RepresentationMask = 0b1111,

    /// <summary>
    /// Allows the world object to become translucent when it obscures the player.
    /// This bit is authored on mixed sprites such as trees, roofs, walls, and arches.
    /// </summary>
    AllowsTransparency = 0b1_0000,
}

/// <summary>Known descriptor-state bits stored at Items.pak model-descriptor offset 0x31.</summary>
[Flags]
public enum SacredItemDescriptorFlags : byte
{
    None = 0,

    /// <summary>
    /// Marks a populated model descriptor. This bit is set on every populated
    /// record in the Sacred Gold Items.pak except the reserved entry zero.
    /// </summary>
    Present = 0x01,
}

/// <summary>
/// Gameplay/UI category stored at Items.pak model-descriptor offset 0x2E.
/// Values are shared by inventory behavior and broad item families; this is
/// separate from <see cref="SacredItemGraphicType"/> and Weapon.pak equipment types.
/// </summary>
public enum SacredItemCategory : byte
{
    Unspecified = 0,
    WorldObject = 1,
    Creature = 3,
    Container = 4,
    Weapon = 5,
    ChestArmor = 6,
    Ring = 8,
    Potion = 9,
    Door = 10,
    Effect = 12,
    Shield = 13,
    Key = 14,
    QuestItem = 15,
    Book = 16,
    Helmet = 17,
    FootArmor = 18,
    Belt = 19,
    Amulet = 20,
    ShoulderArmor = 21,
    ArmArmor = 22,
    LegArmor = 23,
    Gloves = 24,
    Wings = 25,
    SmithingAction = 26,
    Projectile = 27,
    Rune = 28,
    HorseEquipment = 29,
    DwarfCannon = 33,
}
