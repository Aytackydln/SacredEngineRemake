using Raiqub.Generators.EnumUtilities;

namespace Sacred.Core.Weapon;

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
    LongHandled25 = 25,
    FootArmor = 26,
    Gloves = 27,
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
    Unarmed
}

[EnumGenerator]
public enum SacredEquipmentSlot
{
    Unknown,
    MainHand,
    Ring,
    Head,
    Arms,
    Shoulders,
    Chest,
    Belt,
    Legs,
    Feet,
    Hands
}

[EnumGenerator]
public enum SacredEquipmentHandedness
{
    Unknown,
    NotApplicable,
    OneHanded,
    TwoHanded
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
    // Observed on unique rows: 0x0f, 0x4f, 0x8f, 0xcf.
    public bool IsUnique => RarityTierCode == 0x0F;

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

    public SacredEquipmentLore InferLore(ushort short2)
    {
        return EquipmentType switch
        {
            SacredEquipmentType.Sword or SacredEquipmentType.TwoHandedSword => SacredEquipmentLore.Sword,
            SacredEquipmentType.TwoHandedAxe or SacredEquipmentType.OneHandedAxeOrMace => SacredEquipmentLore.Axe,
            SacredEquipmentType.Bow or SacredEquipmentType.Crossbow => SacredEquipmentLore.Bow,
            SacredEquipmentType.Blade => SacredEquipmentLore.Blade,
            SacredEquipmentType.LongHandled21 or SacredEquipmentType.BattleStaff or SacredEquipmentType.MageStaff
                or SacredEquipmentType.LongHandled25 => SacredEquipmentLore.LongHandled,
            SacredEquipmentType.ChestArmor or SacredEquipmentType.Ring or SacredEquipmentType.HeadArmor or SacredEquipmentType.ArmArmor
                or SacredEquipmentType.LegArmor or SacredEquipmentType.Belt or SacredEquipmentType.FootArmor or SacredEquipmentType.Shoulder
                or SacredEquipmentType.Misc => SacredEquipmentLore.Armor,
            SacredEquipmentType.Gloves => SacredEquipmentLore.Armor,
            _ => SacredEquipmentLore.Unknown
        };
    }

    public SacredEquipmentSlot InferSlot()
    {
        return EquipmentType switch
        {
            SacredEquipmentType.Sword or SacredEquipmentType.TwoHandedSword or SacredEquipmentType.TwoHandedAxe
                or SacredEquipmentType.Bow or SacredEquipmentType.Crossbow or SacredEquipmentType.Blade
                or SacredEquipmentType.LongHandled21 or SacredEquipmentType.OneHandedAxeOrMace
                or SacredEquipmentType.BattleStaff or SacredEquipmentType.MageStaff or SacredEquipmentType.LongHandled25
                => SacredEquipmentSlot.MainHand,
            SacredEquipmentType.Ring => SacredEquipmentSlot.Ring,
            SacredEquipmentType.HeadArmor => SacredEquipmentSlot.Head,
            SacredEquipmentType.ArmArmor => SacredEquipmentSlot.Arms,
            SacredEquipmentType.Shoulder => SacredEquipmentSlot.Shoulders,
            SacredEquipmentType.ChestArmor => SacredEquipmentSlot.Chest,
            SacredEquipmentType.Belt => SacredEquipmentSlot.Belt,
            SacredEquipmentType.LegArmor => SacredEquipmentSlot.Legs,
            SacredEquipmentType.FootArmor => SacredEquipmentSlot.Feet,
            SacredEquipmentType.Gloves => SacredEquipmentSlot.Hands,
            _ => SacredEquipmentSlot.Unknown
        };
    }

    public SacredEquipmentHandedness InferHandedness(byte usageIdentifier, ushort short2)
    {
        var slot = InferSlot();
        if (slot != SacredEquipmentSlot.MainHand)
        {
            return SacredEquipmentHandedness.NotApplicable;
        }

        if (EquipmentType == SacredEquipmentType.Gloves)
        {
            return short2 switch
            {
                49225 => SacredEquipmentHandedness.TwoHanded,
                16544 => SacredEquipmentHandedness.OneHanded,
                _ => SacredEquipmentHandedness.Unknown
            };
        }

        return usageIdentifier switch
        {
            1 => SacredEquipmentHandedness.OneHanded,
            2 or 3 or 4 or 7 or 12 => SacredEquipmentHandedness.TwoHanded,
            // Observed blade entries share usage code 10 while differing in handedness.
            _ => SacredEquipmentHandedness.Unknown
        };
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

    private static SacredEquipmentLore InferHandsLore(ushort short2)
    {
        return short2 switch
        {
            0 => SacredEquipmentLore.Armor,
            49225 => SacredEquipmentLore.Unarmed,
            16544 => SacredEquipmentLore.Blade,
            _ => SacredEquipmentLore.Unknown
        };
    }
}
