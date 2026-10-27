using Sacred.Assets.Equipment;
using Sacred.Core.Loot;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Equipment;

/// <summary>Set choices use the same current shared records as ordinary loot.
/// Runtime hero/equipment probes remain explicit context; this creates no item.</summary>
public sealed class WorldCreatureLootSetSelection
{
    private readonly SacredCreatureLootSetSelection _selection;
    public WorldCreatureLootSetSelection(SacredEquipmentGenerationData data, WorldEquipmentSharedValues shared)
    {
        _selection = new(data.Sets, data.CreatureLootSetRules ?? throw new InvalidOperationException("Original set-selection rules unavailable."),
            data.Classes, data.CategoryRules ?? throw new InvalidOperationException("Original class masks unavailable."),
            data.Tuning.Levels.LevelScale, Lookup, resource => data.Modifiers.Find(resource)?.Definition.WeaponType);
        SacredCreatureLootSetTemplate? Lookup(uint resource)
        {
            if (!data.Templates.TryGetSharedTemplate(resource, out var template)) return null;
            var values = shared.Candidate(template.ItemId).Values;
            return new(values.ItemType, template.BaseItemId, values.Flags, values.MinimumLevel, values.Level, template.SetType);
        }
    }
    public SacredCreatureLootSetResult? Select(uint resource, uint selector, bool forced,
        SacredCreatureLootSetContext? hero, SacredRandomSequence random) => _selection.Select(resource, selector, forced, hero, random);
}
