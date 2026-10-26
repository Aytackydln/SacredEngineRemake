using Sacred.Assets.Equipment;
using Sacred.Core.Loot;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Equipment;

/// <summary>Repeatable independent ordinary equipment rolls with a fixed category,
/// uniform exploration, or an original category-stage distribution. Source drop
/// chances, quality selection and world effects belong to their caller.</summary>
public sealed class WorldEquipmentDropSimulator(SacredEquipmentGenerationData data)
{
    private readonly WorldDropCategorySelection _categories = new(data);
    public static IReadOnlyList<SacredItemCategory> Categories { get; } = Array.AsReadOnly(new[]
    {
        SacredItemCategory.Weapon, SacredItemCategory.ChestArmor, SacredItemCategory.Ring, SacredItemCategory.Shield,
        SacredItemCategory.Helmet, SacredItemCategory.FootArmor, SacredItemCategory.Belt, SacredItemCategory.Amulet,
        SacredItemCategory.ShoulderArmor, SacredItemCategory.ArmArmor, SacredItemCategory.LegArmor,
        SacredItemCategory.Gloves, SacredItemCategory.Wings, SacredItemCategory.DwarfCannon
    });

    public SacredEquipmentDropResult Roll(SacredEquipmentDropRequest request)
    {
        if (request.HeroLevel == 0) throw new ArgumentOutOfRangeException(nameof(request), "Hero level must be at least one.");
        if (request.Difficulty > 4) throw new ArgumentOutOfRangeException(nameof(request), "Select one of the five original difficulties.");
        if (request.HeroClass > 9) throw new ArgumentOutOfRangeException(nameof(request), "Select an original hero class or any class.");
        if (request.Quality is > 14) throw new ArgumentOutOfRangeException(nameof(request), "Choose random rarity or quality 0..14.");
        if (request.RarityContext is { } context && (!Enum.IsDefined(context.Profile) || !float.IsFinite(context.EquipmentFindBonus) || context.UnlockMask > 15))
            throw new ArgumentOutOfRangeException(nameof(request), "Invalid rarity context.");
        if (!Enum.IsDefined(request.CategoryMode)) throw new ArgumentOutOfRangeException(nameof(request), "Unknown category mode.");
        if (request.Category.HasValue && request.CategoryMode != SacredDropCategoryMode.Uniform)
            throw new ArgumentException("Choose a category or a weighted preset.", nameof(request));
        if (request.Category is { } specified && !Categories.Contains(specified))
            throw new ArgumentOutOfRangeException(nameof(request), "This category requires its separate generation path.");
        var random = new SacredRandomSequence(request.Seed);
        var category = request.Category ?? _categories.SelectEquipment(request.CategoryMode, request.HeroClass, random);
        if (!Categories.Contains(category))
            return new(request, category, null, random.State, ReadOnlyMemory<byte>.Empty,
                category == SacredItemCategory.Unspecified ? "No eligible native category." :
                $"{category} was rolled. Its item generation path is not implemented; this category was retained without rerolling.");
        var selection = new WorldEquipmentSelection(data);
        var type = selection.Select(category, new SacredEquipmentSelectionCriteria(request.HeroLevel, HeroClass: request.HeroClass, ExcludeSets: true), random);
        if (!type.HasValue) return new(request, category, null, random.State, ReadOnlyMemory<byte>.Empty);
        SacredLootRarityResult? sourceRarity = request.Quality is null && request.RarityContext is { } rarityContext
            ? new WorldLootRarity(data).Roll(rarityContext, request.HeroLevel, request.Difficulty, random) : null;
        if (request.RarityContext?.Profile == SacredLootRarityProfile.Creature && sourceRarity is { RequiresSpecialResource: true })
            return new(request, category, null, random.State, ReadOnlyMemory<byte>.Empty,
                $"Creature rarity selector {sourceRarity.Value.Selector} requires special-resource selection, which is not implemented.") { SourceRarity = sourceRarity };
        var quality = request.Quality ?? sourceRarity?.Quality ?? SacredEquipmentRaritySelection.Roll(
            (data.Tuning.Magic ?? throw new InvalidOperationException("Original rarity weights unavailable.")).RarityWeights, random);
        var generator = new WorldEquipmentOrdinaryGeneration(data) { Naming = data.Naming is null ? null : new WorldEquipmentNames(data) };
        var values = generator.Generate(type.Value, request.HeroLevel, request.Difficulty, selection.Shared.Copy(type.Value), random, quality);
        return new(request, category, type, random.State, values, RolledQuality: quality) { SourceRarity = sourceRarity, Name = generator.GeneratedName };
    }
}
