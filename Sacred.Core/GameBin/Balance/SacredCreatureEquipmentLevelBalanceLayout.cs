using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.Balance;

/// <summary>CalcResults equipment-level tuning in Balance.bin's counted global
/// block. Gold 0x57C7B1 multiplies the highest spell-bonus item level by this
/// factor before the cast-speed penalty. The unrounded threshold is compared
/// with actor level before truncating the required level for skill coverage.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredCreatureEquipmentLevelBalanceLayout
{
    public const int Size = 0x820;
    [FieldOffset(0x81C)] public readonly float SpellBonusItemLevelFactor;
}
