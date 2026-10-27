using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.Balance;

/// <summary>CalcResults tuning in Balance.bin's counted global block, Gold
/// 0xAD4490 + these offsets. Inputs are divisors, not final stat multipliers.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredCreatureResultBalanceLayout
{
    public const int Size = 64;
    [FieldOffset(0x0C)] public readonly float PhysicalProtectionDivisor;
    [FieldOffset(0x10)] public readonly float FireProtectionDivisor;
    [FieldOffset(0x14)] public readonly float MagicProtectionDivisor;
    [FieldOffset(0x18)] public readonly float PoisonProtectionDivisor;
    /// <summary>Growth starts here; Gold interpolates toward 156 over 156 levels,
    /// then adds nine before scaling runtime Basis and allocated points.</summary>
    [FieldOffset(0x1C)] public readonly float AttributeGrowthStart;
    [FieldOffset(0x20)] public readonly float PhysicalDamageDivisor;
    [FieldOffset(0x24)] public readonly float FireDamageDivisor;
    [FieldOffset(0x28)] public readonly float MagicDamageDivisor;
    [FieldOffset(0x2C)] public readonly float PoisonDamageDivisor;
    [FieldOffset(0x30)] public readonly float PhysicalProtectionMultiplierDivisor;
    [FieldOffset(0x34)] public readonly float FireProtectionMultiplierDivisor;
    [FieldOffset(0x38)] public readonly float MagicProtectionMultiplierDivisor;
    [FieldOffset(0x3C)] public readonly float PoisonProtectionMultiplierDivisor;
}
