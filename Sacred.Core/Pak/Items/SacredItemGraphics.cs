namespace Sacred.Core.Pak.Items;

/// <summary>
/// Rendering flags packed with <see cref="SacredItemGraphicType"/> in the first
/// four bytes of an Items.pak model descriptor.
/// </summary>
[Flags]
public enum SacredItemGraphicFlags : uint
{
    None = 0,

    /// <summary>Places the graphic in a rear render layer.</summary>
    RearLayer = 0x0000_0004,

    /// <summary>Uses the extended mixed-sprite behavior.</summary>
    ExtendedMixedSprite = 0x0001_0000,

    /// <summary>Marks the graphic as a light emitter.</summary>
    LightEmitting = 0x0002_0000,

    /// <summary>Scrolls an equipment multitexture fill.</summary>
    MultitextureScroll = 0x0010_0000,

    /// <summary>Scrolls an effect texture vertically.</summary>
    VerticalTextureScroll = 0x0020_0000,

    /// <summary>Places the graphic in a front render layer.</summary>
    FrontLayer = 0x0080_0000,
}

/// <summary>Low-nibble graphic representation stored in Items.pak descriptor bytes 0x00..0x03.</summary>
public enum SacredItemGraphicType : byte
{
    None = 0,
    Model = 2,
    AnimatedMiniObject = 8,
    MixedSpriteOrLightMarker = 9,
    StaticMiniObject = 12,
    RearMixedSprite = 13,
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
