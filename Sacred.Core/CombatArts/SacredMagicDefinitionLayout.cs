using System.Runtime.InteropServices;

namespace Sacred.Core.CombatArts;

/// <summary>Recovered fields of Gold's 148-byte static magic-definition table.
/// Command 6 reads stat curves; 0x4D26B0 evaluates capped parameters. Unmapped bytes remain unnamed.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredMagicDefinitionLayout
{
    public const int Size = 0x94;
    [FieldOffset(0)] public readonly ushort Code;
    [FieldOffset(0x52)] public readonly SacredCombatArtLevelCurveLayout AttackPercent;
    [FieldOffset(0x56)] public readonly SacredCombatArtLevelCurveLayout DefensePercent;
    [FieldOffset(0x5A)] public readonly SacredCombatArtLevelCurveLayout PhysicalProtection;
    [FieldOffset(0x5E)] public readonly SacredCombatArtLevelCurveLayout FireProtection;
    [FieldOffset(0x62)] public readonly SacredCombatArtLevelCurveLayout MagicProtection;
    [FieldOffset(0x66)] public readonly SacredCombatArtLevelCurveLayout PoisonProtection;
    [FieldOffset(0x6A)] public readonly SacredCombatArtLevelCurveLayout CastingSpeedPercent;
    [FieldOffset(0x6E)] public readonly SacredCombatArtLevelCurveLayout MagicRegenerationPercent;
    [FieldOffset(0x72)] public readonly SacredCombatArtLevelCurveLayout HealthRegenerationPercent;
    [FieldOffset(0x76)] public readonly SacredCombatArtLevelCurveLayout ParameterZero;
    [FieldOffset(0x7A)] public readonly SacredCombatArtLevelCurveLayout ParameterOne;
    [FieldOffset(0x7E)] public readonly SacredCombatArtLevelCurveLayout ParameterTwo;
    [FieldOffset(0x82)] public readonly ushort ParameterZeroCap;
    [FieldOffset(0x84)] public readonly ushort ParameterOneCap;
    [FieldOffset(0x86)] public readonly ushort ParameterTwoCap;
}
