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
    /// <summary>Percentage factor applied when generating damage/protection.</summary>
    [FieldOffset(24)] public readonly int Modifier;
    [FieldOffset(28), BinaryUnknown] public readonly int Variance;
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
    [FieldOffset(156)] public readonly int MinimumLevel;
    [FieldOffset(160)] public readonly SacredModifierRange Requirement0;
    [FieldOffset(172)] public readonly SacredModifierRange Requirement1;
    [FieldOffset(184)] public readonly SacredModifierRange Requirement2;
    /// <summary>Native requirement selector; 1 Strength, 2 Dexterity, 3 Endurance, 4 Charisma.</summary>
    [FieldOffset(196)] public readonly int RequirementType0;
    [FieldOffset(200)] public readonly int RequirementType1;
    [FieldOffset(204)] public readonly int RequirementType2;
    [FieldOffset(208), BinaryUnknown] public readonly ushort SpellMask;
    [FieldOffset(210), BinaryUnknown] public readonly ushort SpellCount;
}
