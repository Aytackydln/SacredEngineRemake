using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.GameBin.Balance;

/// <summary>Gold Balance.bin begins with a uint byte count and this raw global block.
/// Gold 0x711F64/0x5F3720 copies it to 0xAD4490; movement reads 0xAD4B8C.</summary>
public static class SacredBalanceLayout
{
    public const int MovementScaleOffset = 0x6FC;
    /// <summary>Gold skill 11 (Armor Lore), fifth curve at 0xAD4680: base, divisor and limit.</summary>
    public const int ArmorLoreMovementCurveOffset = 0x1F0;
    public const int VectorRecordSize = 12;
    public const int PairRecordSize = 8;
    public const int MovementProfileRecordSize = 32; // uint key followed by the payload below.
}

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 4)]
public readonly struct SacredBalanceHeaderLayout
{
    [FieldOffset(0)] public readonly uint GlobalBlockByteCount;
}

/// <summary>Verified fields in the raw global block; the serialized byte count can exceed this prefix.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = MinimumSize)]
public readonly struct SacredBalanceGlobalsLayout
{
    public const int MinimumSize = SacredBalanceLayout.MovementScaleOffset + sizeof(float);
    [FieldOffset(SacredBalanceLayout.ArmorLoreMovementCurveOffset)] public readonly SacredSkillCurveLayout ArmorLoreMovement;
    [FieldOffset(SacredBalanceLayout.MovementScaleOffset)] public readonly float MovementScale;
}

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredSkillCurveLayout
{
    public const int Size = 12;
    [FieldOffset(0)] public readonly float Base;
    [FieldOffset(4)] public readonly float Divisor;
    [FieldOffset(8)] public readonly float Limit;
}

/// <summary>Seven-word payload in the fourth map after the global block.
/// Gold 0x712135 loads it into 0xAD57D4; CalcResults uses word zero for movement.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredMovementProfileLayout
{
    public const int Size = 28;
    [FieldOffset(0)] public readonly int MovementSpeedPercent;
    [FieldOffset(4), BinaryUnknown] public readonly int Word04;
    [FieldOffset(8), BinaryUnknown] public readonly int Word08;
    [FieldOffset(12), BinaryUnknown] public readonly int Word0C;
    [FieldOffset(16), BinaryUnknown] public readonly int Word10;
    [FieldOffset(20), BinaryUnknown] public readonly int Word14;
    [FieldOffset(24), BinaryUnknown] public readonly int Word18;
}
