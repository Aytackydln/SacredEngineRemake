using Sacred.Core.Loot;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Equipment;

public sealed record WorldCreatureLootUniqueEffects(Func<bool> RetireInitial, Func<bool> DestroyInitial,
    Func<uint, byte?> CreateReplacement, Func<int, bool> Upgrade,
    Func<SacredCreatureLootGenerationChoice, bool> Normalize);
public enum WorldCreatureLootUniquePending { None, Retirement, Destruction, Creation, Upgrade, Normalization }
public readonly record struct WorldCreatureLootUniqueResult(WorldCreatureLootUniquePending Pending, uint? Replacement = null)
{
    public bool Completed => Pending == WorldCreatureLootUniquePending.None;
}

/// <summary>Gold selector-zero decisions after its modifier-family query.
/// Initial object, current shared candidates and resolved generation bounds are
/// caller-owned. Effects share RNG; pending results require a fresh replay.</summary>
public static class WorldCreatureLootUniqueReplacement
{
    public static WorldCreatureLootUniqueResult Run(IReadOnlyList<uint>? eligible,
        SacredCreatureLootGenerationLevels levels, SacredRandomSequence random, WorldCreatureLootUniqueEffects effects)
    {
        // Failed modifier lookup skips this selector's generation body entirely.
        if (eligible is null) return new(WorldCreatureLootUniquePending.None);
        if (eligible.Count == 0 || random.Next() % 100 >= 30)
        {
            var generation = SacredCreatureLootGeneration.UniqueFallback(levels, random);
            return new(effects.Normalize(generation) ? WorldCreatureLootUniquePending.None : WorldCreatureLootUniquePending.Normalization);
        }
        if (!effects.RetireInitial()) return new(WorldCreatureLootUniquePending.Retirement);
        if (!effects.DestroyInitial()) return new(WorldCreatureLootUniquePending.Destruction);
        var resource = eligible[SacredRandomRange.Inclusive(0, eligible.Count - 1, random)];
        var spawn = effects.CreateReplacement(resource);
        if (!spawn.HasValue) return new(WorldCreatureLootUniquePending.Creation, resource);
        var level = SacredCreatureLootGeneration.UpgradeLevel(levels.Minimum, spawn.Value);
        return new(effects.Upgrade(level) ? WorldCreatureLootUniquePending.None : WorldCreatureLootUniquePending.Upgrade, resource);
    }
}
