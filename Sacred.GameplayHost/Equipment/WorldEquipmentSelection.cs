using Sacred.Assets.Equipment;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Equipment;

/// <summary>Gold category selection with session-owned shared mutations.
/// This selects a resource; it does not determine an enemy's drop probability.</summary>
public sealed class WorldEquipmentSelection(SacredEquipmentGenerationData data)
{
    private readonly Dictionary<SacredItemCategory, uint[]> _pools = new();
    public WorldEquipmentSharedValues Shared { get; } = new(data);

    public uint? Select(SacredItemCategory category, SacredEquipmentSelectionCriteria criteria, SacredRandomSequence random)
    {
        if (criteria.HeroClass > 9) throw new ArgumentOutOfRangeException(nameof(criteria), "Offline selection supports native hero class selectors 0..9.");
        return SacredEquipmentSelectionDraw.ChooseOffline(Prepare(category, criteria, random), criteria.HeroClass, random);
    }

    public SacredEquipmentSelectionResult SelectLive(SacredItemCategory category, SacredEquipmentSelectionCriteria criteria,
        SacredRandomSequence random, bool localHeroPresent, SacredEquipmentFitEffects effects)
    {
        var preferences = data.SelectionPreferences ?? throw new InvalidOperationException("Original resource preference rules unavailable.");
        return SacredEquipmentSelectionDraw.Choose(Prepare(category, criteria, random), criteria.HeroClass,
            random, localHeroPresent, effects, preferences);
    }

    private IReadOnlyList<SacredEquipmentSelectionCandidate> Prepare(SacredItemCategory category,
        SacredEquipmentSelectionCriteria criteria, SacredRandomSequence random)
    {
        if (category == SacredItemCategory.Book) throw new InvalidOperationException("Book shared-price initialization requires its native default path.");
        if (!_pools.TryGetValue(category, out var pool))
        {
            var entries = data.Templates.Equipment.Keys.Select(type =>
                new SacredEquipmentSelectionPoolEntry(data.Templates.GetItem(type).ModelDesc, Shared.Candidate(type)));
            pool = SacredEquipmentSelectionPool.Build(entries, category, data.Pools).Select(candidate => candidate.ItemType).ToArray();
            _pools.Add(category, pool);
        }
        var candidates = pool.Select(Shared.Candidate).ToArray();
        return SacredEquipmentSelectionFilter.Apply(candidates, category, criteria, random, Shared.Prepare, Shared.InferClasses);
    }
}
