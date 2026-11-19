using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.Balance;

/// <summary>Reached acquired-art timer curves in Balance.bin's counted global
/// block. Native 566070 supplies these to 565DC0 as base/divisor/limit triples.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x1288)]
public readonly struct SacredCombatArtTimerBalanceLayout
{
    [FieldOffset(0x160)] public readonly SacredSkillCurveLayout Skill25Regeneration;
    [FieldOffset(0x127C)] public readonly SacredSkillCurveLayout Skill32Regeneration;
}
