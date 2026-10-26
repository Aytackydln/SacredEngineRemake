using System.Linq;
using Sacred.GameplayHost.Equipment;
using Sacred.Inventory.Items;

namespace AssetViewer.AssetViewer;

internal sealed record ItemDropHistoryRow(int Number, SacredEquipmentDropResult Result, SacredItemInstance? Item, string Name)
{
    private string CategoryLabel => ItemDropCategoryChoice.All.Single(choice =>
        choice.Mode == Result.Request.CategoryMode && choice.Category == Result.Request.Category).Name;
    private string RarityLabel => Result.Request.Quality is { } quality ? $"fixed quality {quality}"
        : Result.Request.RarityContext?.Profile is { } profile ? $"{profile} rarity" : "base rarity";
    public override string ToString() => Item is null
        ? $"#{Number} · {Result.Category} · {Name} · {CategoryLabel} · {RarityLabel} · seed {Result.Request.Seed}"
        : $"#{Number} · {Name} · level {Item.Level}, rarity {Result.RarityTier} · {CategoryLabel} · {RarityLabel} · seed {Result.Request.Seed}";
}
