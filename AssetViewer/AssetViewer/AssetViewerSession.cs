using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AssetViewer.ItemViewer;
using Sacred.Assets;
using Sacred.Assets.Paks.Models;
using Sacred.Assets.Paks.Texture;
using Sacred.Core;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Granny.Loading;

namespace AssetViewer.AssetViewer;

internal sealed class AssetViewerSession : IDisposable
{
    private AssetViewerSession(string directory, SacredGameData data, ModelsPakArchive models, TexturePakArchive textures)
    {
        GameDirectory = directory;
        Data = data;
        Models = models;
        Textures = textures;
        Items = data.GamePakStore.Items.Values.OrderBy(item => item.ItemIndex).ToArray();
        Equipment = data.GamePakStore.Weapons.Values.ToArray();
    }

    public string GameDirectory { get; }
    public SacredGameData Data { get; }
    public ModelsPakArchive Models { get; }
    public TexturePakArchive Textures { get; }
    public ItemsPakEntry[] Items { get; }
    public SacredEquipment[] Equipment { get; }

    public static Task<AssetViewerSession> LoadAsync(string directory) => Task.Run(() =>
    {
        directory = Path.GetFullPath(directory.Trim().Trim('"'));
        var pak = Path.Combine(directory, "pak");
        var settings = SacredItemFilterSaveStore.CreateDefault().Load();
        var data = SacredGameData.LoadFromGamePaks(CreateDirectories(directory));
        var models = ModelsPakArchive.Load(Path.Combine(pak, "models.pak"), Path.Combine(pak, "models.tmp"),
            GrnAssetLoaderFactory.Create(settings.GrannyBackend, directory));
        TexturePakArchive? textures = null;
        try
        {
            textures = TexturePakArchive.LoadFromDirectory(pak);
            var session = new AssetViewerSession(directory, data, models, textures);
            Console.WriteLine($"[Assets] Loaded {session.Items.Length} items, {session.Equipment.Length} equipment, {textures.Entries.Count} textures from {directory}.");
            return session;
        }
        catch
        {
            models.Dispose();
            textures?.Dispose();
            throw;
        }
    });

    public static SacredGameDirectories CreateDirectories(string directory) => new()
    {
        GlobalResourcesPath = Path.Combine(directory, "scripts", "us", "global.res"),
        WeaponsPakPath = Path.Combine(directory, "pak", "Weapon.pak"),
        ItemsPakPath = Path.Combine(directory, "pak", "Items.pak"),
        TexturesPakPath = Path.Combine(directory, "pak", "texture.pak")
    };

    public void Dispose()
    {
        Models.Dispose();
        Textures.Dispose();
    }
}
