using Sacred.Core.Runtime;

namespace Sacred.Core.Pak.Weapon;

/// <summary>Gold 0x4AD51E..0x4AD5CB's base rarity draw, with neutral
/// magic-find and survival multipliers. Weights come from Balance.bin's second
/// map. Resource-level set/unique selection is outside this quality roll.</summary>
public static class SacredEquipmentRaritySelection
{
    public static uint Roll(IReadOnlyDictionary<uint, uint> weights, SacredRandomSequence random)
    {
        var draw = random.Next() % 10000 + 1;
        uint cumulative = 0;
        foreach (var row in weights.OrderBy(row => row.Key))
        {
            cumulative = unchecked(cumulative + (uint)(ushort)row.Value * 10);
            // Native compares strictly; the last boundary can fall through.
            if (cumulative > draw) return row.Key is > 1 and < 16 ? 16 - row.Key : 0;
        }
        return 0;
    }
}
