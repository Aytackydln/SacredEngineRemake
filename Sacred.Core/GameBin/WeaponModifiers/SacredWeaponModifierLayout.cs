using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.GameBin.WeaponModifiers;

/// <summary>Gold wpmod.bin fixed header. Followed by a uint count and 24-byte bonuses.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredWeaponModifierLayout
{
    public const int Size = 212;
    public const int MaximumItemCount = 5;

    [FieldOffset(0)] public readonly uint ItemId0;
    [FieldOffset(4)] public readonly uint ItemId1;
    [FieldOffset(8)] public readonly uint ItemId2;
    [FieldOffset(12)] public readonly uint ItemId3;
    [FieldOffset(16)] public readonly uint ItemId4;
    [FieldOffset(20)] public readonly int ItemCount;
    /// <summary>Percentage factor applied when generating damage/protection. Also the over-level
    /// armor movement penalty coefficient (Gold CalcResults 0x57A067), valid only in 1..999.</summary>
    [FieldOffset(24)] public readonly int Modifier;
    /// <summary>Damage spread percentage (Gold 0x5D042D): minimum = value * (1 - spread),
    /// maximum = value * (1 + spread), truncated separately. Spread is capped at 100.0,
    /// after multiplying by the native single-precision 0.01 constant.</summary>
    [FieldOffset(28)] public readonly int Variance;
    [FieldOffset(32)] public readonly SacredModifierRange PhysicalDamage;
    [FieldOffset(44)] public readonly SacredModifierRange FireDamage;
    [FieldOffset(56)] public readonly SacredModifierRange MagicDamage;
    [FieldOffset(68)] public readonly SacredModifierRange PoisonDamage;
    [FieldOffset(80)] public readonly SacredModifierRange PhysicalProtection;
    [FieldOffset(92)] public readonly SacredModifierRange FireProtection;
    [FieldOffset(104)] public readonly SacredModifierRange MagicProtection;
    [FieldOffset(116)] public readonly SacredModifierRange PoisonProtection;
    [FieldOffset(128)] public readonly SacredModifierRange Attack;
    [FieldOffset(140)] public readonly SacredModifierRange Defense;
    [FieldOffset(152)] public readonly int WeaponType;
    /// <summary>Minimum generation level after multiplication by Balance.bin +0x111C.
    /// Gold 0x5D52D0 raises the level after computing the combat level factor.</summary>
    [FieldOffset(156)] public readonly int MinimumLevel;
    /// <summary>Compatibility view of the first eligibility row; Minimum is the level divisor,
    /// and Maximum is not read by Gold's eligibility generation stage.</summary>
    [FieldOffset(164)] public readonly SacredModifierRange Requirement0;
    [FieldOffset(176)] public readonly SacredModifierRange Requirement1;
    [FieldOffset(188)] public readonly SacredModifierRange Requirement2;
    [FieldOffset(160), BinaryUnknown] public readonly int WordA0;
    [FieldOffset(164)] public readonly SacredEquipmentEligibilityLayout Eligibility0;
    [FieldOffset(176)] public readonly SacredEquipmentEligibilityLayout Eligibility1;
    [FieldOffset(188)] public readonly SacredEquipmentEligibilityLayout Eligibility2;
    /// <summary>Gold 0x5D54F5 eligibility selector: 10..17 adds a class flag; values
    /// above 100 store skill (selector - 100) and generated rank. Not an attribute selector.</summary>
    [FieldOffset(200)] public readonly int RequirementType0;
    [FieldOffset(204)] public readonly int RequirementType1;
    [FieldOffset(208)] public readonly int RequirementType2;
}
