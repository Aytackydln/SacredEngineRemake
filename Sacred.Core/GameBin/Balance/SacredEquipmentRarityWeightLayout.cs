using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.Balance;

/// <summary>Balance.bin second counted map, copied by Gold 0x5CF4B0
/// to the eight-byte rarity vector. Native copies only the weight's low word.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredEquipmentRarityWeightLayout
{
    public const int SerializedSize = 8;
    [FieldOffset(0)] public readonly uint Selector;
    [FieldOffset(4)] public readonly uint Weight;
    /// <summary>In the script/base quality path, selectors 2..15 map to quality
    /// 14..1; other selectors produce zero quality at this stage. Their separate
    /// source/resource behaviors must not be inferred from the quality value.</summary>
    public uint Quality => Selector is > 1 and < 16 ? 16 - Selector : 0;
}
