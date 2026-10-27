using Sacred.Core.Runtime;

namespace Sacred.Core.Loot;

/// <summary>Gold 0x43B1A0: six rows by three difficulty variants. The native
/// quality override is applied after the initial random quality draws.</summary>
public static class SacredPotionLootSelection
{
    public static uint Select(IReadOnlyList<uint> table, uint difficulty, bool restricted,
        int qualityOverride, SacredRandomSequence random)
    {
        if (table.Count != 18) throw new ArgumentException("Original potion table requires six rows and three variants.", nameof(table));
        var row = 5;
        if (random.Next() % 1000 > 600)
        {
            var minimum = restricted ? 2 : 0;
            row = random.Next() % (5 - minimum) + minimum;
        }
        if (qualityOverride is >= 0 and < 6) row = qualityOverride;
        var variant = difficulty switch { 0 => 0, 1 => random.Next() % 2, 2 => random.Next() % 2 + 1, _ => 2 };
        return table[row * 3 + variant];
    }
}
