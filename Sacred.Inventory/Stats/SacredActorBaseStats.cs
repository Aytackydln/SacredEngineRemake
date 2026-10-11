using System;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;

namespace Sacred.Inventory.Stats;

/// <summary>The six runtime Basis words, before growth, allocations or equipment.</summary>
public sealed record SacredActorBaseStats(ushort Strength, ushort Endurance, ushort Dexterity,
    ushort PhysicalRegeneration, ushort MentalRegeneration, ushort Charisma)
{
    public static SacredActorBaseStats FromTemplate(SacredCreatureTemplate? template) => template is { } source
        ? new(source.Strength, source.Endurance, source.Dexterity, source.PhysicalRegeneration, source.MentalRegeneration, source.Charisma)
        : new(0, 0, 0, 0, 0, 0);

    public static SacredActorBaseStats FromNativeValues(in SacredActorValuesLayout values) =>
        new(values.BaseAttributes[0], values.BaseAttributes[1], values.BaseAttributes[2],
            values.BaseAttributes[3], values.BaseAttributes[4], values.BaseAttributes[5]);

    public ushort this[SacredActorStat attribute] => attribute switch
    {
        SacredActorStat.Strength => Strength,
        SacredActorStat.Endurance => Endurance,
        SacredActorStat.Dexterity => Dexterity,
        SacredActorStat.PhysicalRegeneration => PhysicalRegeneration,
        SacredActorStat.MentalRegeneration => MentalRegeneration,
        SacredActorStat.Charisma => Charisma,
        _ => throw new ArgumentOutOfRangeException(nameof(attribute))
    };
}
