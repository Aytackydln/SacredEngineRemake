using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;

namespace Sacred.Core.Pak.Weapon;

/// <summary>Gold 0x5C81CB..0x5C8670: filter one prepared native category pool.
/// Preserves iteration order and RNG draws even for subsequently rejected items.
/// Does not build that pool, initialize shared records, choose among survivors,
/// calculate drop probabilities or generate a concrete item.</summary>
/// <remarks>Optional shared-preparation and class-inference services run at the
/// original call positions. They must return the updated shared candidate;
/// class inference follows the initial class gate and precedes the final draw.</remarks>
public static class SacredEquipmentSelectionFilter
{
    public static IReadOnlyList<SacredEquipmentSelectionCandidate> Apply(
        IReadOnlyList<SacredEquipmentSelectionCandidate> pool, SacredItemCategory category,
        SacredEquipmentSelectionCriteria criteria, SacredRandomSequence random,
        Func<SacredEquipmentSelectionCandidate, SacredEquipmentSelectionCandidate>? prepareShared = null,
        Func<SacredEquipmentSelectionCandidate, SacredEquipmentSelectionCandidate>? inferDefaultClasses = null)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(random);
        var result = new List<SacredEquipmentSelectionCandidate>();
        var level = Math.Min(criteria.Level, (ushort)28);
        var lower = Math.Max(0, (int)(level * .5 - 15));
        foreach (var source in pool)
        {
            var allowance = 1;
            if (level > 5)
            {
                var maximum = level >= 9 ? 3 : level / 3;
                allowance = Roll(maximum, random) * Roll(maximum, random);
            }
            var candidate = prepareShared?.Invoke(source) ?? source;
            // Native draws the allowance before checking usage or modifier presence.
            if (criteria.Usage != 15 && candidate.Usage != criteria.Usage) continue;
            if (candidate.Modifier is not { } modifier) continue;
            var upper = level + allowance;
            if (modifier.MinimumLevel > upper) continue;
            var values = candidate.Values;
            if ((criteria.Flags & 2) == 0 && (byte)category is not (30 or 31) &&
                (values.Level >= upper || values.MinimumLevel >= upper || values.Level < lower)) continue;
            if (!AcceptClass(values.Flags, criteria.HeroClass)) continue;
            candidate = inferDefaultClasses?.Invoke(candidate) ?? candidate;
            values = candidate.Values;
            if (values.ExcludedFromRandomSelection) continue;
            if ((criteria.Flags & 1) != 0 && HasBonus(values)) continue;
            if (modifier.WeaponType == -1 || modifier.MinimumLevel > criteria.Level ||
                unchecked((uint)modifier.SelectionLimitValue) > criteria.MaximumModifierSelectionValue) continue;
            if (criteria.ExcludeSets && candidate.SetType != 0) continue;
            if ((values.Flags & 0xF0000) == 0xF0000 && modifier.WeaponType == 29) continue;
            result.Add(candidate);
        }
        return result;
    }

    private static int Roll(int maximum, SacredRandomSequence random) =>
        maximum == 1 ? 1 : random.Next() % maximum + 1;

    private static bool AcceptClass(uint flags, uint heroClass)
    {
        if (heroClass == 0 || (flags & 0xC0003F) == 0) return true;
        if ((flags & 0x40) != 0) return false;
        var mask = heroClass switch
        {
            >= 1 and <= 6 => 1u << ((int)heroClass - 1),
            7 => 0x20u,
            8 => 0x400000u,
            9 => 0x800000u,
            _ => 0u // Native leaves other class selectors unrestricted at this stage.
        };
        return mask == 0 || (flags & mask) != 0;
    }

    private static bool HasBonus(SacredEquipmentInstanceLayout values)
    {
        for (var i = 0; i < 8; i++) if (values.BonusValues[i] != 0) return true;
        return false;
    }
}
