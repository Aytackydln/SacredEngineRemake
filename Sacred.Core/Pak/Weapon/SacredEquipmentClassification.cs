using Raiqub.Generators.EnumUtilities;

namespace Sacred.Core.Pak.Weapon;

[Flags]
[EnumGenerator]
public enum SacredCharacterClassMask : ushort
{
    None = 0,
    Seraphim = 1 << 0,
    Gladiator = 1 << 1,
    BattleMage = 1 << 2,
    DarkElf = 1 << 3,
    WoodElf = 1 << 4,
    Vampiress = 1 << 5,
    Dwarf = 1 << 6,
    Daemon = 1 << 7,
    AllBase = Seraphim | Gladiator | BattleMage | DarkElf | WoodElf | Vampiress,
    AllKnown = AllBase | Dwarf | Daemon
}

[EnumGenerator]
public enum SacredEquipmentType : byte
{
    Sword = 0,
    Dagger = 1,
    TwoHandedSword = 4,
    Axe = 5,
    TwoHandedAxe = 6,
    Shield = 7,
    Bow = 8,
    Crossbow = 9,
    Blade = 10,
    ChestArmor = 13,
    Ring = 14,
    Amulet = 15,
    HeadArmor = 16,
    ArmArmor = 17,
    LegArmor = 18,
    Belt = 19,
    Shoulder = 20,
    LongHandled21 = 21,
    OneHandedAxeOrMace = 22,
    BattleStaff = 23,
    MageStaff = 24,
    Briddle = 25,
    FootArmor = 26,
    Gloves = 27,
    Wings = 28,
    Misc = 29,
    Pistol = 30,
    Musket = 31,
}

[EnumGenerator]
public enum SacredEquipmentLore
{
    Unknown,
    LongHandled,
    Axe,
    Sword,
    Bow,
    Blade,
    Armor,
    Unarmed,
    Jewelry
}

[EnumGenerator]
public enum SacredEquipmentHandedness
{
    Unknown,
    NotApplicable,
    OneHanded,
    TwoHanded
}

[EnumGenerator]
public enum SacredEquipmentRarityTier : byte
{
    Tier0 = 0x0,
    Tier1 = 0x1,
    Tier2 = 0x2,
    Tier3 = 0x3,
    Tier4 = 0x4,
    Tier5 = 0x5,
    Tier6 = 0x6,
    Tier7 = 0x7,
    Tier8 = 0x8,
    Tier9 = 0x9,
    Tier10 = 0xA,
    Tier11 = 0xB,
    Tier12 = 0xC,
    Tier13 = 0xD,
    Tier14 = 0xE,
    Tier15 = 0xF
}

public readonly record struct SacredEquipmentClassification(
    byte CharacterClassMaskCode,
    SacredCharacterClassMask CharacterClassMask,
    SacredCharacterClassMask EffectiveCharacterClassMask,
    byte EquipmentTypeCode,
    SacredEquipmentType EquipmentType,
    byte RarityAndClassFlags,
    byte RarityTierCode,
    byte ClassFlagCode
)
{
    public SacredEquipmentRarityTier RarityTier => (SacredEquipmentRarityTier)RarityTierCode;

    public static SacredEquipmentClassification FromBytes(
        byte characterClassMaskCode,
        byte equipmentTypeCode,
        byte rarityAndClassFlags
    )
    {
        var characterClassMask = (SacredCharacterClassMask)characterClassMaskCode;
        var classFlagCode = (byte)(rarityAndClassFlags & 0xF0);

        return new SacredEquipmentClassification(
            CharacterClassMaskCode: characterClassMaskCode,
            CharacterClassMask: characterClassMask,
            EffectiveCharacterClassMask: InferEffectiveCharacterClassMask(characterClassMask, classFlagCode),
            EquipmentTypeCode: equipmentTypeCode,
            EquipmentType: (SacredEquipmentType)equipmentTypeCode,
            RarityAndClassFlags: rarityAndClassFlags,
            RarityTierCode: (byte)(rarityAndClassFlags & 0x0F),
            ClassFlagCode: classFlagCode
        );
    }

    /// <summary>
    /// Infers the displayed equipment family. The type code alone is ambiguous for gloves:
    /// weapon gloves occupy two inventory rows, while armor gloves occupy one.
    /// </summary>
    public SacredEquipmentLore InferLore(byte inventoryHeight)
    {
        return EquipmentType switch
        {
            SacredEquipmentType.Sword or SacredEquipmentType.TwoHandedSword => SacredEquipmentLore.Sword,
            SacredEquipmentType.TwoHandedAxe or SacredEquipmentType.OneHandedAxeOrMace => SacredEquipmentLore.Axe,
            SacredEquipmentType.Bow or SacredEquipmentType.Crossbow => SacredEquipmentLore.Bow,
            SacredEquipmentType.Blade => SacredEquipmentLore.Blade,
            SacredEquipmentType.Ring or SacredEquipmentType.Amulet => SacredEquipmentLore.Jewelry,
            SacredEquipmentType.LongHandled21 or SacredEquipmentType.BattleStaff or SacredEquipmentType.MageStaff
                or SacredEquipmentType.Briddle => SacredEquipmentLore.LongHandled,
            SacredEquipmentType.ChestArmor or SacredEquipmentType.HeadArmor or SacredEquipmentType.ArmArmor
                or SacredEquipmentType.LegArmor or SacredEquipmentType.Belt or SacredEquipmentType.FootArmor or SacredEquipmentType.Shoulder
                or SacredEquipmentType.Misc => SacredEquipmentLore.Armor,
            SacredEquipmentType.Gloves when IsUnarmedGloveWeapon(inventoryHeight) => SacredEquipmentLore.Unarmed,
            SacredEquipmentType.Gloves => SacredEquipmentLore.Armor,
            _ => SacredEquipmentLore.Unknown
        };
    }

    public SacredEquipmentHandedness InferHandedness(byte usageIdentifier, byte inventoryHeight)
    {
        if (IsUnarmedGloveWeapon(inventoryHeight))
        {
            return SacredEquipmentHandedness.TwoHanded;
        }

        return usageIdentifier switch
        {
            0 when EquipmentType == SacredEquipmentType.Blade => SacredEquipmentHandedness.OneHanded,
            1 => SacredEquipmentHandedness.OneHanded,
            2 or 3 or 4 or 7 or 12 => SacredEquipmentHandedness.TwoHanded,
            // Observed blade entries share usage code 10 while differing in handedness.
            _ => SacredEquipmentHandedness.Unknown
        };
    }

    private bool IsUnarmedGloveWeapon(byte inventoryHeight)
    {
        return EquipmentType == SacredEquipmentType.Gloves && inventoryHeight == 2;
    }

    private static SacredCharacterClassMask InferEffectiveCharacterClassMask(
        SacredCharacterClassMask characterClassMask,
        byte classFlagCode
    )
    {
        if (characterClassMask != SacredCharacterClassMask.None)
        {
            return characterClassMask;
        }

        return classFlagCode switch
        {
            0x40 => SacredCharacterClassMask.Dwarf,
            0x80 => SacredCharacterClassMask.Daemon,
            0xC0 => SacredCharacterClassMask.AllBase,
            _ => SacredCharacterClassMask.None
        };
    }
}
