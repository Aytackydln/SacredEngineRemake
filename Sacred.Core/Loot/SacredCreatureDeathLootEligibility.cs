using Sacred.Core.GameBin.Balance;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;

namespace Sacred.Core.Loot;

public sealed record SacredCreatureDeathLootRules(uint AttributeToLocalHeroResource, IReadOnlyList<uint> ExcludedResources);
public enum SacredCreatureDeathLootGate { Ready, ContinueDeath, NoKiller, UnresolvedEquipmentDetach }
public readonly record struct SacredCreatureDeathLootEligibilityResult(SacredCreatureDeathLootGate Gate,
    SacredCreatureDeathLootActor? Killer, uint PhysicalFlags);

/// <summary>Gold 0x533BCF..0x533D09. This preserves effects before the killer
/// check; a rejected loot gate does not cancel the remaining death callbacks.</summary>
public static class SacredCreatureDeathLootEligibility
{
    public static SacredCreatureDeathLootEligibilityResult Evaluate(SacredCreatureDeathLootActor source,
        uint physicalFlags, SacredCreatureDeathLootActor? killer, SacredCreatureDeathLootRules rules,
        SacredLootBalance balance, SacredRandomSequence random, Func<SacredCreatureDeathLootActor?> localHero,
        Func<bool> observerCanReceiveLoot, Func<uint, bool> isLocalHeroCompanion, Func<bool> detachEquipment)
    {
        physicalFlags |= 2;
        if (killer is { } attacker && attacker.Reference != source.Reference &&
            attacker.Resource == rules.AttributeToLocalHeroResource && attacker.RelationClass == 0)
            killer = localHero();
        if (killer is { Behavior: var behavior } && (behavior & 0x40000) != 0)
            killer = localHero();
        if (source.OwnerReference is > 0 and <= 16 || !observerCanReceiveLoot() || source.RelationClass == 0 ||
            rules.ExcludedResources.Contains(source.Resource) || source.ResourceClass == SacredCreatureClass.Undead && source.MaximumHitPoints != 0)
            return Result(SacredCreatureDeathLootGate.ContinueDeath);
        // The upper endpoint is inclusive, giving 1001 possible samples.
        if (random.Next() % 1001 < balance.DeathEquipmentDetachChance)
            for (var attempt = 0; attempt < 3; attempt++)
                if (!detachEquipment()) return Result(SacredCreatureDeathLootGate.UnresolvedEquipmentDetach);
        if (killer is null) return Result(SacredCreatureDeathLootGate.NoKiller);
        if (killer.Value.Reference > 16 && (localHero() is null || !isLocalHeroCompanion(killer.Value.Reference)))
            return Result(SacredCreatureDeathLootGate.ContinueDeath);
        return Result(SacredCreatureDeathLootGate.Ready);

        SacredCreatureDeathLootEligibilityResult Result(SacredCreatureDeathLootGate gate) => new(gate, killer, physicalFlags);
    }
}
