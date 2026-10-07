using Raiqub.Generators.EnumUtilities;

namespace Sacred.Core.Pak.Items;

/// <summary>
/// Rendering flags stored at Items.pak model-descriptor offset 0x02.
/// </summary>
[Flags]
[EnumGenerator]
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

    /// <summary>
    /// Selects the final ceiling queue (4), drawn above ordinary world scenery
    /// and models. Native combined flags mask: 0x00800000.
    /// </summary>
    FrontLayer = 0x0080,

    /// <summary>Observed unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte0100 = 0x0100,

    /// <summary>Observed unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte0200 = 0x0200,

    /// <summary>Observed unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Animal = 0x0400,

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
[EnumGenerator]
public enum SacredItemGraphicType : ushort
{
    None = 0,
    
    OverWater = 0b0001,
    Model = 0b0010,
    /// <summary>
    /// Selects floor routing in the world queue builder: Floor (0) for instance
    /// state 1 or flag 0x20, otherwise Floor2 (2). This is separate from the
    /// high-word CastsStaticShadow bit.
    /// </summary>
    Shadowable = 0b0100,
    MiniObject = 0b1000,
    LightMarker = 0b0001,

    /// <summary>
    /// Allows the world object to become translucent when it obscures the player.
    /// This bit is authored on mixed sprites such as trees, roofs, walls, and arches.
    /// </summary>
    AllowsTransparency = 0b1_0000,

    /// <summary>Allows world selection. Demo TypeManager::isSelectable (0x413DE0)
    /// and Gold (0x4263F0) test bit 8 of the descriptor's combined flags,
    /// independently of category.</summary>
    Selectable = 0x0100,

    /// <summary>
    /// Enables sprite depth testing and writes. Native renderObjects tests
    /// combined mask 0x00000200 and computes corner depth from screen Y and
    /// Static.pak's geometric height. Separate from GraphicFlags.Byte0200.
    /// </summary>
    UsesSpriteDepth = 0b10_0000_0000,
}

/// <summary>
/// Gameplay/UI category stored at Items.pak model-descriptor offset 0x2E.
/// Values are shared by inventory behavior and broad item families; this is
/// separate from <see cref="SacredItemGraphicType"/> and Weapon.pak equipment types.
/// </summary>
[EnumGenerator]
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
