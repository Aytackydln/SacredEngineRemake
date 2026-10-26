using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.Balance;

/// <summary>Balance.bin first counted map, 12-byte rows. Gold 0x5D1750
/// filters skill and individual bonus codes using these bounds. The first
/// row instead supplies socket capacity in its third field.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredEquipmentMagicPoolLayout
{
    public const int SerializedSize = 12;
    [FieldOffset(0)] public readonly int FirstBonusCode;
    [FieldOffset(4)] public readonly int LastBonusCode;
    [FieldOffset(8)] public readonly int MaximumSockets;
}
