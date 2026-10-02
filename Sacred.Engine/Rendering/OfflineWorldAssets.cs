using System.IO;
using System.Linq;
using Sacred.Assets.Paks.Items;
using Sacred.Assets.Paks.Mixed;
using Sacred.Assets.Paks.Models;
using Sacred.Assets.Paks.Texture;
using Sacred.Assets.Paks.Tiles;
using Sacred.Engine.Assets;

namespace Sacred.Engine.Rendering;

internal static class OfflineWorldAssets
{
    public static AssetManager Load(string gameDirectory, TexturePakArchive textures, ITextureSource? textureSource)
    {
        var pak = Path.Combine(gameDirectory, "pak");
        ModelsPakArchive? models = null;
        try
        {
            models = ModelsPakArchive.Load(Path.Combine(pak, "models.pak"), Path.Combine(pak, "Models.tmp"));
            return new AssetManager(
                textures,
                TilesPakArchive.Load(Path.Combine(pak, "tiles.pak")),
                ItemsPakArchive.Load(Path.Combine(pak, "Items.pak")).ToArray(),
                [],
                [],
                MixedPakArchive.Load(Path.Combine(pak, "mixed.pak")),
                models)
            {
                LeaveTextureArchiveOpen = true,
                TextureSource = textureSource
            };
        }
        catch
        {
            models?.Dispose();
            throw;
        }
    }
}
