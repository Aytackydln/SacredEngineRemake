using System.Linq;

namespace AssetViewer.AssetViewer;

internal sealed record ItemDropRarityChoice(string Name, uint? Quality)
{
    public override string ToString() => Name;
    public static ItemDropRarityChoice[] All { get; } = new[] { new ItemDropRarityChoice("Random · selected rarity rules", null) }
        .Concat(Enumerable.Range(0, 15).Select(quality => new ItemDropRarityChoice(
            quality == 0 ? "Ordinary · quality 0" : $"Magic · quality {quality}", (uint)quality))).ToArray();
}
