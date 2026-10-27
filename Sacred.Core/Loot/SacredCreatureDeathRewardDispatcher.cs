using Sacred.Core.GameBin.Balance;
using Sacred.Core.GameBin.Sets;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;

namespace Sacred.Core.Loot;

/// <summary>Effects execute at their native call positions and own any RNG
/// consumed by their dependencies. False means unresolved; no later action runs.</summary>
public sealed record SacredCreatureDeathRewardCallbacks(Func<SacredEquipmentRewardRequest, bool> Equipment,
    Func<uint, bool> ForcedDrop, Func<uint, bool> WorldContainsResource, Func<SacredCreatureUniqueRewardQuery, bool> UniqueReward);

public readonly record struct SacredCreatureDeathRewardResult(SacredCreatureDeathLootPlan Plan, bool Completed);

/// <summary>Gold 0x533E11..0x534595. Completes the source-specific dispatch,
/// with equipment generation and forced-drop construction delegated explicitly.</summary>
public static class SacredCreatureDeathRewardDispatcher
{
    public static SacredCreatureDeathRewardResult Run(SacredCreatureDeathLootPlan plan, uint sourceResource,
        ushort sourceLevel, SacredCreatureDeathLootActor killer, IReadOnlyList<SacredSetEntry> sets,
        SacredCreatureDeathRewardRules rules, SacredLootBalance balance, SacredRandomSequence random,
        SacredCreatureDeathRewardCallbacks effects)
    {
        if (!plan.RequiresBoostedSourceDispatch) return new(plan, true);
        plan = plan with { RequiresBoostedSourceDispatch = false };
        if (!rules.Resources.TryGetValue(sourceResource, out var rule)) return new(plan, true);
        if (rule.BonusIndex >= 0)
        {
            var bonuses = balance.DeathRewardBonuses ?? throw new InvalidOperationException("Original death reward tuning unavailable.");
            plan = plan with { Bonus = unchecked(plan.Bonus + bonuses[rule.BonusIndex]) };
        }
        switch (rule.Kind)
        {
            case SacredCreatureDeathRewardKind.Bonus:
                break;
            case SacredCreatureDeathRewardKind.Equipment:
                AddCalls();
                return new(plan, effects.Equipment(rule.Equipment));
            case SacredCreatureDeathRewardKind.ExtraCalls:
                AddCalls();
                break;
            case SacredCreatureDeathRewardKind.ResetBoost:
                plan = plan with { Count = balance.CreatureDeathBaseRollCount, Bonus = unchecked(plan.Bonus - balance.CreatureBoostedRarityBonus) };
                break;
            case SacredCreatureDeathRewardKind.UniqueWorldResource:
                if (SacredRandomRange.Inclusive(0, rule.RandomMaximum, random) == 0 && !effects.WorldContainsResource(rule.Resource))
                    return new(plan, effects.ForcedDrop(rule.Resource));
                break;
            case SacredCreatureDeathRewardKind.SetResource:
                plan = plan with { Count = 0 };
                var setIndex = SacredRandomRange.Inclusive(1, sets.Count - 1, random);
                if (setIndex >= rules.FirstHeroSet && setIndex <= rules.LastHeroSet)
                {
                    var hero = killer.ResourceClass == SacredCreatureClass.Hero ? killer.Resource : (uint)SacredRandomRange.Inclusive(1, 9, random);
                    if (hero is >= 1 and <= 9) return new(plan, effects.ForcedDrop(rules.HeroSetResources[(int)hero - 1]));
                }
                else if ((uint)setIndex < sets.Count)
                {
                    var set = sets[setIndex];
                    var member = SacredRandomRange.Inclusive(0, set.DeclaredItemCount - 1, random);
                    return new(plan, effects.ForcedDrop(set.ItemIds[member]));
                }
                break;
            case SacredCreatureDeathRewardKind.Jackpot:
                plan = plan with { Count = 0 };
                for (var i = 1; i <= 6; i++)
                {
                    var minimum = (uint)(SacredLootRarity.SurvivalCurve(i, 6, 4, 13) + .5f);
                    var maximum = Math.Min(14, minimum + (uint)SacredRandomRange.Inclusive(1, 2, random));
                    if (!effects.Equipment(new(1000, minimum, maximum, 25))) return new(plan, false);
                }
                if (SacredRandomRange.Inclusive(0, 99, random) < 33)
                {
                    var hero = SacredRandomRange.Inclusive(0, 1, random) == 0 ? killer.Resource : 0;
                    var category = rules.JackpotCategories[SacredRandomRange.Inclusive(0, rules.JackpotCategories.Count - 1, random)];
                    if (!effects.UniqueReward(new(category, sourceLevel, hero))) return new(plan, false);
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(rule.Kind), rule.Kind, "Unsupported death reward kind.");
        }
        return new(plan, true);

        void AddCalls() => plan = plan with { Count = unchecked(plan.Count + (uint)SacredRandomRange.Inclusive(rule.MinimumExtraCalls, rule.MaximumExtraCalls, random)) };
    }
}
