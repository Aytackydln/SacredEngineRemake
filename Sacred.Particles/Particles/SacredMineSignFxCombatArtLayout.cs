using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Fields consumed by Gold 0x4D26E0 with parameter indices 0 and 1.
/// Each unsigned word pair defines base + level * increment. The helper truncates
/// their ratio (or the numerator when denominator is zero), applies the nonzero
/// parameter-zero cap to its unsigned low word; initializer keeps
/// its low 16 bits, multiplies by event +0x70, and truncates again.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x84)]
public readonly struct SacredMineSignFxCombatArtLayout
{
    [FieldOffset(0x76)] public readonly ushort NumeratorBase;
    [FieldOffset(0x78)] public readonly ushort NumeratorPerLevel;
    [FieldOffset(0x7A)] public readonly ushort DenominatorBase;
    [FieldOffset(0x7C)] public readonly ushort DenominatorPerLevel;
    [FieldOffset(0x82)] public readonly ushort NumeratorCap;
}
