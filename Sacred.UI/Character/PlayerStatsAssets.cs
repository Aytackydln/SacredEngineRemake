using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.UI;

namespace Sacred.UI.Character;

/// <summary>Original statistics artwork loaded independently of equipment-slot assets.</summary>
public sealed class PlayerStatsAssets
{
    public PlayerStatsLayout Layout { get; }
    public IReadOnlyDictionary<string, TextureAsset> Textures { get; }

    private PlayerStatsAssets(PlayerStatsLayout layout, Dictionary<string, TextureAsset> textures)
    { Layout = layout; Textures = textures; }

    public static async Task<PlayerStatsAssets> LoadAsync(
        IReadOnlyList<SacredExecutableUiTextureDefinitionLayout> definitions,
        Func<string, Task<TextureAsset>> loadTexture)
    {
        var layout = new PlayerStatsLayout(definitions);
        var textures = new Dictionary<string, TextureAsset>(StringComparer.OrdinalIgnoreCase);
        var names = layout.Backgrounds.Select(b => b.Art.TextureName)
            .Concat(layout.Decorations.Select(d => d.Art.TextureName)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names) textures.Add(name, await loadTexture(name).ConfigureAwait(false));
        return new(layout, textures);
    }
}
