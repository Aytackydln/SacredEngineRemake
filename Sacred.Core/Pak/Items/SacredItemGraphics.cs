namespace Sacred.Core.Pak.Items;

/// <summary>
/// Rendering flags stored at Items.pak model-descriptor offset 0x02.
/// </summary>
[Flags]
public enum SacredItemGraphicFlags : ushort
{
    None = 0,

    /// <summary>Uses the extended mixed-sprite behavior.</summary>
    ExtendedMixedSprite = 0x0001,

    /// <summary>Marks the graphic as a light emitter.</summary>
    LightEmitting = 0x0002,

    /// <summary>Scrolls an equipment multitexture fill.</summary>
    MultitextureScroll = 0x0010,

    /// <summary>Scrolls an effect texture vertically.</summary>
    VerticalTextureScroll = 0x0020,

    /// <summary>Places the graphic in a front render layer.</summary>
    FrontLayer = 0x0080,
}

/// <summary>
/// Graphic representation stored at Items.pak model-descriptor offset 0x00.
/// The named values are the complete bit patterns observed in populated descriptors;
/// overlapping bits do not independently identify the rendering behavior.
/// </summary>
[Flags]
public enum SacredItemGraphicType : ushort
{
    None = 0,
    Model = 0b0010,
    AnimatedMiniObject = 0b1000,
    MixedSpriteOrLightMarker = 0b1001,
    StaticMiniObject = 0b1100,
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
