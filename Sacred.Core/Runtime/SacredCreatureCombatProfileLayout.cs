using System.Runtime.InteropServices;

namespace Sacred.Core.Runtime;

/// <summary>64-byte original executable result profile, indexed by the character
/// component's +0x126 byte. Only the first six fields are interpreted here.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredCreatureCombatProfileLayout
{
    public const int Size = 64;
    [FieldOffset(0)] public readonly float AttackStrengthPercent;
    [FieldOffset(4)] public readonly float AttackDexterityPercent;
    [FieldOffset(8)] public readonly float EquipmentAttackPercent;
    [FieldOffset(12)] public readonly float DefenseStrengthPercent;
    [FieldOffset(16)] public readonly float DefenseDexterityPercent;
    [FieldOffset(20)] public readonly float EquipmentDefensePercent;
}
