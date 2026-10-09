using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sacred.Assets.Executable;
using Sacred.Assets.Paks.Texture;

namespace Sacred.UI.Maps;

public sealed class WorldMapUiAssets
{
    public WorldMapUiLayout Layout { get; }
    public IReadOnlyDictionary<string, TextureAsset> Textures { get; }

    private WorldMapUiAssets(WorldMapUiLayout layout, Dictionary<string, TextureAsset> textures)
    { Layout = layout; Textures = textures; }

    public static async Task<WorldMapUiAssets> LoadAsync(string gameDirectory, Func<string, Task<TextureAsset>> load)
    {
        var path = Path.Combine(gameDirectory, "Sacred.exe");
        if (!File.Exists(path)) path = Path.Combine(gameDirectory, "pureHD.exe");
        var image = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        var layout = new WorldMapUiLayout(SacredUiTextureTable.Read(image), image.AsSpan().IndexOf("UI_ESC_EXPORT\0"u8) >= 0);
        var names = layout.Buttons.SelectMany(b => new[] { b.Up.TextureName, b.Down.TextureName })
            .Concat(new[] { layout.LeftPanel.TextureName, layout.RightPanel.TextureName })
            .Concat(layout.Frame is { } frame ? new[] { frame.TextureName } : Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var textures = await Task.WhenAll(names.Select(load)).ConfigureAwait(false);
        return new(layout, names.Select((name, index) => (name, texture: textures[index]))
            .ToDictionary(p => p.name, p => p.texture, StringComparer.OrdinalIgnoreCase));
    }
}
