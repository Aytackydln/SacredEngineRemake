using System.Runtime.InteropServices;

namespace Sacred.Core.CombatArts;

/// <summary>Native UInt16 base/per-level pair. Evaluation wraps to UInt16 before any cap.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 4)]
public readonly struct SacredCombatArtLevelCurveLayout
{
    public readonly ushort Base;
    public readonly ushort PerLevel;
}
