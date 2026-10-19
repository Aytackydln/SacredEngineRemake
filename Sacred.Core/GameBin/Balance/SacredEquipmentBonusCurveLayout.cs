using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.GameBin.Balance;

/// <summary>Gold 0x5E22B0 selects a 24-byte Balance.bin bonus curve.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredEquipmentBonusCurveLayout
{
    public const int Size = 24;
    /// <summary>Maximum authored value for a bonus group with nonzero high word (0x5D4F3A).</summary>
    [FieldOffset(0)] public readonly float PercentageMaximum;
    [FieldOffset(4), BinaryUnknown] public readonly float Word04;
    /// <summary>Below native float epsilon, use FlatMaximum; otherwise scale authored
    /// value by (level + 9) * difficultyFactor * 0.01f (0x5D4F6C).</summary>
    [FieldOffset(8)] public readonly float LevelScaling;
    [FieldOffset(12)] public readonly float FlatMaximum;
    [FieldOffset(16), BinaryUnknown] public readonly float Word10;
    [FieldOffset(20), BinaryUnknown] public readonly float Word14;
}
