using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;

namespace Sacred.Core.Loot;

public enum SacredCreatureLootEntryKind { Proceed, Destroyed, HeroWithoutForcedResource }
public readonly record struct SacredCreatureLootEntryResult(SacredCreatureLootEntryKind Kind, uint ForcedResource);

/// <summary>Gold 0x559BF0..0x559D54. Authored lookup precedes the destroyed
/// check and may reseed RNG. Hero progression forcing precedes hero suppression.</summary>
public static class SacredCreatureLootEntry
{
    public static SacredCreatureLootEntryResult Resolve(uint forcedResource, uint sourceResource, bool destroyed,
        SacredCreatureClass sourceClass, byte heroProgression22, IReadOnlyList<uint> progressionResources,
        SacredRandomSequence random, Func<uint, uint> authoredResource)
    {
        if (forcedResource == 0) forcedResource = authoredResource(sourceResource);
        if (destroyed) return new(SacredCreatureLootEntryKind.Destroyed, forcedResource);
        if (forcedResource == 0 && heroProgression22 >= 5 && random.Next() % 10 == 9)
        {
            var draw = random.Next() % 10;
            forcedResource = progressionResources[Math.Min(draw, 4)];
        }
        return new(sourceClass == SacredCreatureClass.Hero && forcedResource == 0
            ? SacredCreatureLootEntryKind.HeroWithoutForcedResource : SacredCreatureLootEntryKind.Proceed, forcedResource);
    }
}
