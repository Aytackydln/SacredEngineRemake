namespace Sacred.Assets.GameBin;

/// <summary>Gold's literal ECS_ operand dispatch in 0x472BC0. These are interpreter
/// selectors, independent of item IDs and playable combat-art resource IDs.</summary>
public static class SacredScriptCreatureSpellSelector
{
    private static readonly Dictionary<string, uint> Selectors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ECS_TELEPORT"] = 1, ["ECS_SUPERFLUID"] = 4, ["ECS_FIRESTRIKE"] = 5,
        ["ECS_PENTAGRAMMKILLER"] = 6, ["ECS_HEALING"] = 7, ["ECS_KUGELBLITZ"] = 8,
        ["ECS_FIRECIRCLE"] = 11, ["ECS_GROUNDPOISON"] = 12, ["ECS_LIGHTNING_STRIKE"] = 13,
        ["ECS_FREEZE"] = 14, ["ECS_HOLYLIGHT"] = 15, ["ECS_HOLYSHIELD"] = 22,
        ["ECS_FIRESKIN"] = 24, ["ECS_METEOR"] = 26, ["ECS_TWISTER"] = 28,
        ["ECS_WINDSTRIKE"] = 29, ["ECS_FLASH"] = 33, ["ECS_ICEBULLETS"] = 34,
        ["ECS_CIRCLEOFICE"] = 35, ["ECS_CHANGELING"] = 36, ["ECS_BUBBLETRAP"] = 38,
        ["ECS_KILLERPLANTS"] = 39, ["ECS_PLANTCAGE"] = 40, ["ECS_LOSTSOULS"] = 41,
        ["ECS_NATUREHEALING"] = 42, ["ECS_REANIMATE"] = 43, ["ECS_NOSPELL"] = uint.MaxValue
    };

    public static uint Resolve(string name, Func<string, uint?>? resolveType = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        const string summon = "ECS_BIGSUMMON";
        if (name.StartsWith(summon, StringComparison.OrdinalIgnoreCase))
        {
            var suffix = name[summon.Length..];
            if (suffix.Length == 0 || suffix[0] == ' ') return 3;
            var type = resolveType?.Invoke(suffix) ??
                throw new InvalidDataException($"Creature spell selector '{name}' requires the original item-name resolver.");
            return unchecked(3u | type << 16);
        }
        return Selectors.GetValueOrDefault(name, 0xFFu);
    }
}
