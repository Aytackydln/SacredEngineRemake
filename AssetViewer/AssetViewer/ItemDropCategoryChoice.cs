using System;
using System.Linq;
using Sacred.Core.Pak.Items;
using Sacred.GameplayHost.Equipment;

namespace AssetViewer.AssetViewer;

internal sealed record ItemDropCategoryChoice(string Name, SacredItemCategory? Category, SacredDropCategoryMode Mode,
    string Description, string? Command = null)
{
    public override string ToString() => Name;
    public static ItemDropCategoryChoice[] All { get; } = new[]
    {
        new ItemDropCategoryChoice("Random · equal categories", null, SacredDropCategoryMode.Uniform,
            "Each of the 14 supported equipment categories has equal probability.", "random"),
        new("Equipment · native weights", null, SacredDropCategoryMode.NativeWeighted,
            "Original relative weights and class eligibility, restricted to supported equipment. Books, potions, runes and other non-equipment paths are excluded.", "weighted"),
        new("Reward equipment weights", null, SacredDropCategoryMode.Reward,
            "Original reward category list: weapons occupy 5 of 19 slots, amulets and rings 2 each, each armor category 1. Samples the equipment branch only.", "reward"),
        new("Creature drop · armor branch", null, SacredDropCategoryMode.CreatureArmor,
            "Original armor branch, conditioned on supported equipment. Horse equipment is excluded. Samples this branch after its drop-chance check.", "armor"),
        new("Creature drop · jewelry branch", null, SacredDropCategoryMode.CreatureJewelry,
            "Original jewelry branch: two ring slots and one amulet slot. Samples this branch after its drop-chance check.", "jewelry")
    }.Concat(WorldEquipmentDropSimulator.Categories.Select(category =>
        new ItemDropCategoryChoice(category.ToString(), category, SacredDropCategoryMode.Uniform, "Always use this category."))).ToArray();

    public static ItemDropCategoryChoice Parse(string text) => All.SingleOrDefault(choice =>
        string.Equals(choice.Command ?? choice.Category?.ToString(), text, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException("Choose random, weighted, reward, armor, jewelry, or a supported category.");
}
