using Sacred.Assets.Paks.Texture;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.World.Map;

/// <summary>Native renderIcons dispatch for the four merc.bin service types.</summary>
public static class WorldMapNpcTextures
{
    public static async Task<IReadOnlyDictionary<SacredRegionNpcKind, TextureAsset>> LoadAsync(
        Func<string, CancellationToken, Task<TextureAsset>> load, CancellationToken cancellationToken = default)
    {
        var names = new Dictionary<SacredRegionNpcKind, string>
        {
            [SacredRegionNpcKind.Merchant] = "MOUSE_TRADER.TGA",
            [SacredRegionNpcKind.Blacksmith] = "MOUSE_BLACKSMITH.TGA",
            [SacredRegionNpcKind.ComboMaster] = "MOUSE_COMBO.TGA",
            [SacredRegionNpcKind.HorseTrader] = "MOUSE_HORSETRADER.TGA"
        };
        var results = await Task.WhenAll(names.Select(async pair =>
            new KeyValuePair<SacredRegionNpcKind, TextureAsset>(pair.Key,
                await load(pair.Value, cancellationToken).ConfigureAwait(false)))).ConfigureAwait(false);
        return results.ToDictionary();
    }
}
