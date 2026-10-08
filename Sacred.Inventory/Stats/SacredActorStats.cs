using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace Sacred.Inventory.Stats;

/// <summary>Immutable snapshot shared by inherent values, equipment contributions and their sum.</summary>
public sealed class SacredActorStats
{
    public static SacredActorStats Empty { get; } = new(new Dictionary<SacredActorStat, double>(),
        new Dictionary<byte, int>(), new Dictionary<ushort, int>());

    internal SacredActorStats(IDictionary<SacredActorStat, double> values,
        IDictionary<byte, int> skills, IDictionary<ushort, int> combatArts)
    {
        Values = values.ToFrozenDictionary();
        Skills = skills.ToFrozenDictionary();
        CombatArts = combatArts.ToFrozenDictionary();
    }

    public FrozenDictionary<SacredActorStat, double> Values { get; }
    public FrozenDictionary<byte, int> Skills { get; }
    public FrozenDictionary<ushort, int> CombatArts { get; }
    public double this[SacredActorStat stat] => Values.GetValueOrDefault(stat);

    public static SacredActorStats operator +(SacredActorStats left, SacredActorStats right) => new(
        left.Values.Keys.Concat(right.Values.Keys).Distinct().ToDictionary(key => key, key => left[key] + right[key]),
        left.Skills.Keys.Concat(right.Skills.Keys).Distinct().ToDictionary(key => key,
            key => left.Skills.GetValueOrDefault(key) + right.Skills.GetValueOrDefault(key)),
        left.CombatArts.Keys.Concat(right.CombatArts.Keys).Distinct().ToDictionary(key => key,
            key => left.CombatArts.GetValueOrDefault(key) + right.CombatArts.GetValueOrDefault(key)));
}
