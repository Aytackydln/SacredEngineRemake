using System.Runtime.InteropServices;

namespace Sacred.Core.CombatArts;

/// <summary>Native sSpellEntry (Demo and Gold): acquired rank and equipment rank are distinct bytes.
/// This maps runtime state; it does not establish a save-file wrapper.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredCombatArtRuntimeLayout
{
    public const int Size = 22;
    [FieldOffset(0)] public readonly SacredCombatArtKind Kind;
    [FieldOffset(4)] public readonly ushort Code;
    [FieldOffset(6)] public readonly byte BaseLevel;
    [FieldOffset(7)] public readonly byte BonusLevel;
    [FieldOffset(8)] public readonly ushort Flags;
    [FieldOffset(10)] public readonly float TimerMaximum;
    [FieldOffset(14)] public readonly float TimerFactor;
    [FieldOffset(18)] public readonly float Timer;
}
