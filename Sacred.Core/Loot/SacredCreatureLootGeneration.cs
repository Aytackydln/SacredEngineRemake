using Sacred.Core.GameBin.Balance;
using Sacred.Core.Runtime;

namespace Sacred.Core.Loot;

public readonly record struct SacredCreatureLootGenerationLevels(int Minimum, int Maximum);
public enum SacredCreatureLootGenerationKind { Normalize, Upgrade, UniqueFamilyRequired }
public readonly record struct SacredCreatureLootGenerationChoice(SacredCreatureLootGenerationKind Kind,
    int Level = 0, byte Quality = 0, byte Sockets = 0);

/// <summary>Gold 0x55B4DE..0x55BD37 equipment generation decisions after a
/// concrete item/value record exists. Does not create, replace or upgrade it.</summary>
public static class SacredCreatureLootGeneration
{
    public static SacredCreatureLootGenerationLevels Levels(ushort sourceLevel, uint difficulty, SacredCreatureBalance balance)
    {
        var index = SacredCreatureProgressionRules.DifficultyIndex(difficulty);
        // Unlike the health-colour reverse-level helper, this path has neither
        // its +0.5 nor its level-one floor, and preserves signed subtraction.
        var minimum = unchecked((int)((sourceLevel - balance.LevelAdditions[index]) /
            (1d + balance.LevelPercentAdditions[index] * (double).01f)));
        return new(minimum, unchecked((int)(minimum * (double)1.05f + .5d)));
    }

    public static SacredCreatureLootGenerationChoice Choose(SacredCreatureLootGenerationLevels levels,
        uint selector, bool forcedResource, byte existingSpawnLevel, SacredRandomSequence random)
    {
        if (selector == 0 && !forcedResource) return new(SacredCreatureLootGenerationKind.UniqueFamilyRequired);
        if (selector <= 1) return new(SacredCreatureLootGenerationKind.Upgrade, UpgradeLevel(levels.Minimum, existingSpawnLevel));
        var sockets = RollSockets(random);
        var level = selector switch
        {
            16 => levels.Minimum,
            17 => RollLevel(levels.Minimum / 2, levels.Minimum, random),
            _ => RollLevel(levels.Minimum, levels.Maximum, random)
        };
        var quality = selector is >= 2 and <= 15 ? (byte)(16 - selector) : (byte)0;
        return new(SacredCreatureLootGenerationKind.Normalize, level, quality, sockets);
    }

    /// <summary>Selector zero's ordinary fallback after the family query and
    /// its 30% unique replacement decision have already consumed their draws.</summary>
    public static SacredCreatureLootGenerationChoice UniqueFallback(SacredCreatureLootGenerationLevels levels, SacredRandomSequence random)
    {
        var sockets = RollSockets(random);
        return new(SacredCreatureLootGenerationKind.Normalize, RollLevel(levels.Minimum, levels.Maximum, random), 14, sockets);
    }

    public static int UpgradeLevel(int minimum, byte existingSpawnLevel)
    {
        var step = Math.Max(15d, existingSpawnLevel * .5d);
        var count = unchecked((int)((Math.Max(minimum, existingSpawnLevel) - existingSpawnLevel) / step + .5d));
        return unchecked((int)(existingSpawnLevel + count * step));
    }

    private static int RollLevel(int minimum, int maximum, SacredRandomSequence random)
    {
        // The native branch tests one sample, then draws again if it was >=1.
        // Its second sample is not clamped. Both draws matter even if discarded.
        var first = SacredRandomRange.Inclusive(minimum, maximum, random);
        return first < 1 ? 1 : SacredRandomRange.Inclusive(minimum, maximum, random);
    }

    private static byte RollSockets(SacredRandomSequence random)
    {
        if (random.Next() % 4 != 0) return 0;
        if (random.Next() % 4 != 0) return 1;
        return random.Next() % 4 != 0 ? (byte)3 : (byte)2;
    }
}
