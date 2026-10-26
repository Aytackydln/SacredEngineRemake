using Sacred.Core.Runtime;

namespace Sacred.Core.Pak.Items;

public readonly record struct SacredItemCategoryWeight(SacredItemCategory Category, ushort Weight);

/// <summary>Original category data supplied by the executable reader. These
/// distributions describe category choices, not the surrounding drop chances.</summary>
public sealed record SacredItemCategoryRules(
    IReadOnlyList<SacredItemCategoryWeight> Weights, IReadOnlyList<uint> ClassMasks,
    uint AnyClassMask, float RandomScale,
    IReadOnlyList<SacredItemCategory> RewardCategories,
    IReadOnlyList<SacredItemCategory> CreatureArmorCategories,
    SacredItemCategory ArmorRetryCategory,
    IReadOnlyList<SacredItemCategory> CreatureJewelryCategories);

public static class SacredItemCategorySelection
{
    /// <summary>Gold 0x43B090. Availability is the OR of initial shared class
    /// flags for the category (0x5C7D20), before pricing or class inference.</summary>
    public static SacredItemCategory Weighted(SacredItemCategoryRules rules,
        IReadOnlyDictionary<SacredItemCategory, uint> availability, uint heroClass, SacredRandomSequence random)
    {
        var mask = heroClass is >= 1 and <= 9 ? rules.ClassMasks[(int)heroClass - 1] : rules.AnyClassMask;
        uint total = 0;
        foreach (var row in rules.Weights)
            if ((availability.GetValueOrDefault(row.Category) & mask) != 0) total += row.Weight;
        // Original sums to a word and performs its x87 operations at double
        // precision; rounding to float here changes cumulative boundaries.
        var draw = (ushort)(random.Next() * (double)rules.RandomScale * (ushort)total);
        uint cumulative = 0;
        foreach (var row in rules.Weights)
        {
            if ((availability.GetValueOrDefault(row.Category) & mask) == 0) continue;
            cumulative += row.Weight;
            if (draw < cumulative) return row.Category;
        }
        return SacredItemCategory.Unspecified;
    }

    /// <summary>Original repeated-slot arrays. A retry is exactly one extra
    /// draw, even if the replacement has the same category.</summary>
    public static SacredItemCategory Slots(IReadOnlyList<SacredItemCategory> slots,
        SacredRandomSequence random, SacredItemCategory? retryCategory = null)
    {
        if (slots.Count == 0) throw new ArgumentException("Category slots are empty.", nameof(slots));
        var category = slots[random.Next() % slots.Count];
        return category == retryCategory ? slots[random.Next() % slots.Count] : category;
    }
}
