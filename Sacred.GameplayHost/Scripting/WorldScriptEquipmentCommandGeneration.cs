using Sacred.Assets.Equipment;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Equipment;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x4820BC..0x482118 ordinary EquipItem generation after item
/// lookup/RTTI. This caller uses the reverse difficulty level and fixed quality
/// zero; it does not consume CreateNPC's two leading quality/legacy draws.
/// Construction, eligibility, installation and publication are separate.</summary>
public static class WorldScriptEquipmentCommandGeneration
{
    public static void Apply(WorldEquipmentInitialItem item, ushort actorLevel, uint difficulty,
        SacredCreatureBalance progression, SacredEquipmentGenerationData data, SacredRandomSequence random)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(random);
        if (item.Generated || (item.Physical.Base.Flags & 2) == 0 || item.Physical.AttachmentParent != 0)
            throw new InvalidOperationException("EquipItem generation requires its fresh unplaced, unattached item.");
        // The native own-value getter normalizes the original damage bounds
        // before returning +0x118, even though generation replaces the record.
        SacredEquipmentDamageBounds.Normalize(item.Values);
        var level = SacredCreatureProgressionRules.BaseLevelForDifficulty(actorLevel, difficulty, progression, SacredX87Precision.Single);
        var generator = new WorldEquipmentOrdinaryGeneration(data) { Naming = new WorldEquipmentNames(data), Precision = SacredX87Precision.Single };
        var values = generator.Generate(item.Physical.Base.ObjectType, checked((ushort)level), difficulty, item.Values, random, quality: 0);
        var name = generator.GeneratedName ?? throw new InvalidDataException("EquipItem generation requires original object-context naming.");
        item.Values = values;
        item.Name = name;
        item.Physical.Base = item.Physical.Base with { Word3C = name.NativeReference };
        item.Quality = 0;
        item.LegacyGenerationSelector = 0;
        item.Generated = true;
    }
}
