using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.GameBin.WeaponModifiers;

/// <summary>One wpmod.bin eligibility row, interpreted at Gold 0x5D5494.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 12)]
public readonly struct SacredEquipmentEligibilityLayout
{
    /// <summary>Chance out of 100. Values greater than 100 skip the draw entirely.</summary>
    [FieldOffset(0)] public readonly int Chance;
    /// <summary>Rank = truncate(level / divisor + 0.5). Zero divisor defaults to five.</summary>
    [FieldOffset(4)] public readonly int LevelDivisor;
    [FieldOffset(8), BinaryUnknown] public readonly int Word08;
}
