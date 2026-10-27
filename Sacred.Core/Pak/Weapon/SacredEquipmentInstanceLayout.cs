using System.Runtime.InteropServices;

namespace Sacred.Core.Pak.Weapon;

/// <summary>Native sWeaponInfo: the 130-byte mutable item-value block. Weapon.pak embeds
/// it at +128; cItemBase owns a resolved copy and merged socket records. Demo save writes
/// this block separately from sWeaponInfoShared. These values are outcomes, not generation rules.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredEquipmentInstanceLayout
{
    public const int Size = 130;
    /// <summary>Resource retained in the value record. Gold serializes it independently of
    /// the parent object's current visual resource; the two need not be equal.</summary>
    [FieldOffset(0)] public readonly uint ItemType;
    /// <summary>Gold pricing 0x5D3D90 tests bit 31 as its initialized SpawnLevel latch;
    /// an unset bit permits level reconstruction, optional bonus renormalization
    /// and legacy minimum-level adjustment, then 0x5D49C2 sets the bit.</summary>
    [FieldOffset(4)] public readonly uint Flags;
    /// <summary>Gold 0x5D56A0 stores difficulty + 1 in the low nibble and sets
    /// bit 4 for generated values. Pricing sets bit 7 as the level-init latch.
    /// The description maps difficulty codes 2..5 to the Silver..Niobium emblems;
    /// other codes select the first (Bronze) emblem.</summary>
    [FieldOffset(7)] public readonly byte GenerationFlags;
    /// <summary>Gold 0x5C84B1 rejects this value flag during random item selection.</summary>
    public bool ExcludedFromRandomSelection => (Flags & 0x100000) != 0;
    /// <summary>Concrete native class bits: six base heroes at bits 0..5,
    /// Dwarf/Daemon at bits 22/23. This does not infer classes from shared visuals.</summary>
    public SacredCharacterClassMask Classes => (SacredCharacterClassMask)((Flags & 0x3F) | ((Flags >> 16) & 0xC0));
    /// <summary>Generated subtype stored by 0x5D5203 from wpmod WeaponType;
    /// valid as a weapon subtype only for the Weapon descriptor family.</summary>
    public byte EquipmentTypeCode => unchecked((byte)(Flags >> 8));
    [FieldOffset(8)] public readonly uint Price;
    /// <summary>Eight parent socket-kind bytes. Gold 0x5BE9E0 enables merged record i only
    /// when this byte is 1, 2, 3 or 4; 3/4 share returned socket category 3.</summary>
    [FieldOffset(12)] public readonly SacredEquipmentSlotTypes SlotTypes;
    /// <summary>Gold 0x54ABE0 compares level and the four following attribute
    /// bytes against unsigned hero words. Class fit may mutate Flags before a
    /// requirement fails; absence of the value getter skips these comparisons.</summary>
    [FieldOffset(20)] public readonly byte MinimumLevel;
    [FieldOffset(21)] public readonly byte MinimumStrength;
    [FieldOffset(22)] public readonly byte MinimumDexterity;
    [FieldOffset(23)] public readonly byte MinimumCharisma;
    [FieldOffset(24)] public readonly byte MinimumEndurance;
    /// <summary>Native SpawnLevel: the concrete level on a resolved runtime item.</summary>
    [FieldOffset(25)] public readonly byte Level;
    [FieldOffset(26)] public readonly byte MinimumSkill;
    [FieldOffset(27)] public readonly byte MinimumSkillLevel;
    /// <summary>Four minimum words followed by four maximum words. Gold weapon FX
    /// sign-extends the maxima at +36/+38/+40/+42 (cItemBase +13C/+13E/+140/+142).</summary>
    [FieldOffset(28)] public readonly SacredEquipmentDamage Damage;
    [FieldOffset(44)] public readonly short Attack;
    [FieldOffset(46)] public readonly short Defense;
    [FieldOffset(48)] public readonly short BW;
    [FieldOffset(50)] public readonly short PhysicalProtection;
    [FieldOffset(52)] public readonly short FireProtection;
    [FieldOffset(54)] public readonly short MagicProtection;
    [FieldOffset(56)] public readonly short PoisonProtection;
    [FieldOffset(58)] public readonly SacredEquipmentBonusTypes BonusTypes;
    [FieldOffset(74)] public readonly SacredEquipmentBonusGroups BonusGroups;
    [FieldOffset(106)] public readonly SacredEquipmentBonusValues BonusValues;
    [FieldOffset(122)] public readonly SacredEquipmentLegacyRequirements LegacyRequirements;
    /// <summary>Pricing 0x5D49B8 updates this nonzero legacy minimum only before
    /// the bit-31 initialization latch is set. Current minimum is at +20.</summary>
    [FieldOffset(122)] public readonly byte LegacyMinimumLevel;
    [FieldOffset(129)] public readonly byte BlacksmithLevel;
}
