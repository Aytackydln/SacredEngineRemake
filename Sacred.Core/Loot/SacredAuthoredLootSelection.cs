using Sacred.Core.Runtime;

namespace Sacred.Core.Loot;

/// <summary>Resolved original script-manager drop table (+0x762C), in native order.</summary>
public readonly record struct SacredAuthoredLootRule(uint SourceResource, uint ItemResource, uint ChancePercent);

public static class SacredAuthoredLootSelection
{
    /// <summary>Gold 0x4B06D0. The first matching row reseeds the thread's CRT
    /// RNG from time(0), even when its chance or item predicate then rejects it.</summary>
    public static uint Select(IReadOnlyList<SacredAuthoredLootRule> rules, uint sourceResource,
        SacredRandomSequence random, Func<uint> timeSeed, Func<uint, bool> eligibleItem)
    {
        foreach (var row in rules)
        {
            if (row.SourceResource != sourceResource) continue;
            random.Reset(timeSeed());
            if (row.ChancePercent < random.Next() % 100 + 1U || !eligibleItem(row.ItemResource)) return 0;
            return row.ItemResource;
        }
        return 0;
    }
}
