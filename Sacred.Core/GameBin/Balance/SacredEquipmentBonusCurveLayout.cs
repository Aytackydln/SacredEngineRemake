using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.Balance;

/// <summary>Gold 0x5E22B0 selects a 24-byte Balance.bin bonus curve.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredEquipmentBonusCurveLayout
{
    public const int Size = 24;
    /// <summary>Maximum authored value for a bonus group with nonzero high word (0x5D4F3A).
    /// Generated-name strength also divides percentage magnitudes by it (0x5C3EF3).</summary>
    [FieldOffset(0)] public readonly float PercentageMaximum;
    [FieldOffset(4)] public readonly float Word04;
    /// <summary>0x42C113 divides percentage magnitudes by this cost divisor.</summary>
    [FieldOffset(4)] public readonly float PercentageCostDivisor;
    /// <summary>Below native float epsilon, use FlatMaximum; otherwise scale authored
    /// value by (level + 9) * difficultyFactor * 0.01f (0x5D4F6C).</summary>
    [FieldOffset(8)] public readonly float LevelScaling;
    /// <summary>Flat authored maximum; also the flat-bonus name-strength divisor (0x5C3F2A).</summary>
    [FieldOffset(12)] public readonly float FlatMaximum;
    [FieldOffset(16)] public readonly float Word10;
    /// <summary>0x42C1BF divides flat magnitudes by this cost divisor; level-scaled
    /// bonuses additionally multiply it by the item's SpawnLevel + 9.</summary>
    [FieldOffset(16)] public readonly float FlatCostDivisor;
    [FieldOffset(20)] public readonly float Word14;
    /// <summary>0x42C060 multiplies normalization cost/clamp factor by this value for jewelry.
    /// 0x5C3E8A also applies it when deriving jewelry's generated name variant.</summary>
    [FieldOffset(20)] public readonly float JewelryNormalizationFactor;
}
