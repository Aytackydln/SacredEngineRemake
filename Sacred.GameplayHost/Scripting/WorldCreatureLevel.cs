using Sacred.Core.GameBin.Balance;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x56531B..0x56551F, before experience/skills/CalcResults.
/// Template predicates are file-derived; a native type-specific cap, if any, is supplied
/// by the original executable definitions rather than an item-ID exception here.</summary>
public static class WorldCreatureLevel
{
    public static WorldCreatureLevelSelection Resolve(ushort input, SacredCreatureTemplate? template, WorldCreatureLevelContext context,
        SacredCreatureLevelBalance levels, SacredCreatureBalance progression)
    {
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentNullException.ThrowIfNull(progression);
        var index = SacredCreatureProgressionRules.DifficultyIndex(context.Difficulty);
        var behavior = (context.BehaviorFlags & 0x80000) != 0;
        var actorFlag = (context.ActorFlags2B8 & 0x40) != 0;
        var special = behavior || actorFlag;
        uint level = input == 0 && !special ? 1u : input;
        if (!context.SkipDifficultyAdjustment && context.ObjectReference > 1 &&
            (!special || input is not (1 or 1000)))
        {
            if (context.Multiplayer) level = unchecked(level + levels.MultiplayerAddition);
            level = SacredCreatureProgressionRules.NormalizeLevel(unchecked(level + levels.InputAdditions[index]), context.Difficulty, progression);
            if (!special) level = unchecked(level + context.RuntimeLevelAddition);
        }

        if (context.ObjectReference > 16 && !context.IgnoreLimits)
        {
            var maximum = unchecked(levels.MaximumLevels[index] + context.SectorMaximumLevel);
            if (context.TypeMaximumOverride is { } typeMaximum) maximum = typeMaximum;
            var capApplies = !special;
            // Native chooses the first applicable branch. The two additions do not stack.
            if (template is null || (template.Flags & 2) == 0)
            {
                if (behavior) { maximum = unchecked(maximum + levels.BehaviorCapAddition); capApplies = true; }
                else if (actorFlag) { maximum = unchecked(maximum + levels.ActorCapAddition); capApplies = true; }
            }
            if (capApplies && level > maximum) level = maximum;
        }

        if (template?.Class == SacredCreatureClass.Horse || context.IgnoreLimits)
            level = Math.Min(input, (ushort)250);
        return new(level);
    }
}
