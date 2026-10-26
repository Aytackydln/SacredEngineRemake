using System.Runtime.InteropServices;
using Sacred.Assets.Equipment;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Equipment;

/// <summary>Fresh native category availability and category-only caller presets.</summary>
public sealed class WorldDropCategorySelection(SacredEquipmentGenerationData data)
{
    private IReadOnlyDictionary<SacredItemCategory, uint>? _availability;
    private IReadOnlyDictionary<SacredItemCategory, uint>? _equipmentAvailability;
    public IReadOnlyDictionary<SacredItemCategory, uint> Availability => _availability ??= BuildAvailability();

    /// <summary>Viewer equipment scope: retain original relative weights among
    /// supported equipment. Full general-loot category sampling remains Select.</summary>
    public SacredItemCategory SelectEquipment(SacredDropCategoryMode mode, uint heroClass, SacredRandomSequence random)
    {
        if (mode == SacredDropCategoryMode.NativeWeighted)
        {
            _equipmentAvailability ??= Availability.Where(pair => WorldEquipmentDropSimulator.Categories.Contains(pair.Key))
                .ToDictionary();
            return SacredItemCategorySelection.Weighted(data.CategoryRules!, _equipmentAvailability, heroClass, random);
        }
        // Condition this caller branch on supported equipment. The original
        // armor branch's one horse redraw still executes inside each sample.
        SacredItemCategory category;
        do { category = Select(mode, heroClass, random); }
        while (category != SacredItemCategory.Unspecified && !WorldEquipmentDropSimulator.Categories.Contains(category));
        return category;
    }

    public SacredItemCategory Select(SacredDropCategoryMode mode, uint heroClass, SacredRandomSequence random)
    {
        if (mode == SacredDropCategoryMode.Uniform)
            return WorldEquipmentDropSimulator.Categories[random.Next() % WorldEquipmentDropSimulator.Categories.Count];
        var rules = data.CategoryRules ?? throw new InvalidOperationException("Original category definitions unavailable.");
        return mode switch
        {
            SacredDropCategoryMode.NativeWeighted => SacredItemCategorySelection.Weighted(rules, Availability, heroClass, random),
            SacredDropCategoryMode.Reward => SacredItemCategorySelection.Slots(rules.RewardCategories, random),
            SacredDropCategoryMode.CreatureArmor => SacredItemCategorySelection.Slots(rules.CreatureArmorCategories, random, rules.ArmorRetryCategory),
            SacredDropCategoryMode.CreatureJewelry => SacredItemCategorySelection.Slots(rules.CreatureJewelryCategories, random),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    private IReadOnlyDictionary<SacredItemCategory, uint> BuildAvailability()
    {
        // 0x5C7D20 scans descriptors and resolves WeaponEntry, including default
        // row references on non-equipment categories. It does not infer classes.
        var rows = data.Templates.Equipment.Values.ToArray();
        var entries = new List<SacredEquipmentSelectionPoolEntry>();
        foreach (var (type, item) in data.Templates.Items)
        {
            if (!data.Pools.Admits(type) || item.ModelDesc.WeaponEntry >= rows.Length) continue;
            var row = rows[item.ModelDesc.WeaponEntry];
            var values = MemoryMarshal.Read<SacredEquipmentInstanceLayout>(data.Templates.GetInstance(row.ItemId));
            entries.Add(new(item.ModelDesc, new(type, row.UsageIdentifier, row.SetType, values, data.Modifiers.Find(type)?.Definition)));
        }
        return (data.CategoryRules ?? throw new InvalidOperationException("Category definitions unavailable.")).Weights
            .ToDictionary(row => row.Category, row => SacredEquipmentSelectionPool.Build(entries, row.Category, data.Pools)
                .Aggregate(0U, (mask, candidate) => mask | (candidate.Values.Flags & 0xC0003F)));
    }
}
