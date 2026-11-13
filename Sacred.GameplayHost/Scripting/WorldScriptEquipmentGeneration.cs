using Sacred.Assets.Equipment;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Equipment;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x484D8B..0x484DFA, initial explicit equipment generation.
/// The level conversion's returned value is discarded by the caller. Two draws
/// precede generation: the second is quality, the first is legacy argument four.
/// Naming uses the actual item context and updates its instance resource name.</summary>
public static class WorldScriptEquipmentGeneration
{
    public static void Apply(WorldEquipmentInitialItem item, ushort actorLevel, uint difficulty,
        SacredEquipmentGenerationData data, SacredRandomSequence random, SacredX87Precision precision)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(random);
        if (item.Generated || (item.Physical.Base.Flags & 2) == 0 || item.Physical.AttachmentParent != 0)
            throw new InvalidOperationException("Script generation requires a fresh unplaced, unattached item.");
        var generator = new WorldEquipmentOrdinaryGeneration(data) { Naming = new WorldEquipmentNames(data), Precision = precision };
        var legacy = (uint)(random.Next() % 4);
        var quality = (uint)(random.Next() % 4);
        var values = generator.Generate(item.Physical.Base.ObjectType, actorLevel, difficulty, item.Values, random, quality);
        var name = generator.GeneratedName ?? throw new InvalidDataException("Initial script equipment requires original object-context naming.");
        item.Values = values;
        item.Name = name;
        item.Physical.Base = item.Physical.Base with { Word3C = name.NativeReference };
        item.Quality = quality;
        item.LegacyGenerationSelector = legacy;
        item.Generated = true;
    }
}
