using System.Collections.Frozen;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using AssetViewer.AssetViewer;
using Sacred.Assets;
using Sacred.Assets.GameBin.Sets;
using Sacred.Assets.GameBin.WeaponModifiers;
using Sacred.Assets.Paks.Creature;
using Sacred.Assets.Paks.Items;
using Sacred.Assets.Paks.Models;
using Sacred.Assets.Paks.Texture;
using Sacred.Assets.Paks.Weapon;
using Sacred.Core;
using Sacred.Core.GameRes;
using Sacred.Granny.Loading;

namespace AssetViewer.Browser.Files;

internal sealed class BrowserInstallation : IDisposable
{
    private readonly InstallationManifest _manifest;
    private readonly Dictionary<string, InstallationFile> _files;
    private readonly string _directory = "/selected/" + Guid.NewGuid().ToString("N");
    private bool _disposed;

    internal BrowserInstallation(string json)
    {
        _manifest = JsonSerializer.Deserialize(json, InstallationJsonContext.Default.InstallationManifest)
            ?? throw new InvalidDataException("Empty installation selection.");
        _files = _manifest.Entries.ToDictionary(file => RelativePath(file.Path), StringComparer.OrdinalIgnoreCase);
    }

    private static string RelativePath(string path)
    {
        var parts = path.Replace('\\', '/').Split('/');
        if (parts.Length < 2 || parts.Any(part => part is "." or ".."))
            throw new InvalidDataException("Select the Sacred installation directory.");
        return string.Join('/', parts.Skip(1));
    }

    internal Stream Open(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var file = _files.GetValueOrDefault(path) ?? throw new FileNotFoundException($"The selected installation is missing {path}.");
        return new BrowserFileStream(file.Id, file.Length);
    }

    private async Task<byte[]> ReadMetadataAsync(string path, CancellationToken token)
    {
        using var stream = Open(path);
        // Small catalogues are decoded in memory; multi-gigabyte PAKs never enter this path.
        if (stream.Length > 64 * 1024 * 1024) throw new InvalidDataException($"Metadata file is unexpectedly large: {path}.");
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, token);
        return bytes;
    }

    internal async Task<AssetViewerSession> LoadAsync(Action<string> status, CancellationToken token = default)
    {
        status("Loading original catalogues…");
        var itemBytes = await ReadMetadataAsync("pak/Items.pak", token);
        var items = ItemsPakArchive.Load(itemBytes).ToFrozenDictionary(item => item.ItemIndex);
        var weaponBytes = await ReadMetadataAsync("pak/Weapon.pak", token);
        using var weaponsStream = new MemoryStream(weaponBytes, writable: false);
        var weapons = WeaponPakParser.Parse(weaponsStream, items).ToFrozenDictionary(item => item.IdemId);
        var resourcePath = _files.ContainsKey("scripts/us/global.res") ? "scripts/us/global.res"
            : _files.Keys.Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault(path => path.EndsWith("/global.res", StringComparison.OrdinalIgnoreCase))
                ?? throw new FileNotFoundException("The installation has no scripts/<language>/global.res.");
        var resourceBytes = await ReadMetadataAsync(resourcePath, token);
        using var resourcesStream = new MemoryStream(resourceBytes, writable: false);
        var resources = new GameResStore(SacredResUnpack.Read(resourcesStream).DistinctBy(pair => pair.Key)
            .ToFrozenDictionary(pair => pair.Key, pair => pair.Value));
        var creatures = _files.ContainsKey("pak/Creature.pak")
            ? CreaturePakArchive.Parse(await ReadMetadataAsync("pak/Creature.pak", token)) : null;

        // Existing UI helpers inspect small original BIN/EXE files by path. Mount
        // only those metadata files; model, texture and sound archives stay Blob-backed.
        foreach (var path in _files.Keys.Where(path => path.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
                     || path.Equals("Sacred.exe", StringComparison.OrdinalIgnoreCase) || path.Equals("pureHD.exe", StringComparison.OrdinalIgnoreCase)))
        {
            var destination = Path.Combine(_directory, path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllBytesAsync(destination, await ReadMetadataAsync(path, token), token);
        }
        Directory.CreateDirectory(Path.Combine(_directory, "PAK"));
        Directory.CreateDirectory(Path.Combine(_directory, "scripts", "us"));
        await File.WriteAllBytesAsync(Path.Combine(_directory, "PAK", "Items.pak"), itemBytes, token);
        await File.WriteAllBytesAsync(Path.Combine(_directory, "PAK", "Weapon.pak"), weaponBytes, token);
        await File.WriteAllBytesAsync(Path.Combine(_directory, "scripts", "us", "global.res"), resourceBytes, token);
        var setsPath = Path.Combine(_directory, "bin", "sets.bin");
        var modifiersPath = Path.Combine(_directory, "bin", "wpmod.bin");
        var data = SacredGameData.FromResources(new GamePakStore(weapons, items), resources, creatures,
            File.Exists(setsPath) ? SetsBinArchive.Load(setsPath) : [],
            File.Exists(modifiersPath) ? WeaponModifiersBinArchive.Load(modifiersPath) : []);

        status("Indexing model archive ranges…");
        var indexing = Stopwatch.StartNew();
        var modelStream = Open("pak/models.pak");
        ModelsPakArchive? models = null;
        TexturePakArchive? textures = null;
        var textureStreams = new List<(string Name, Stream Stream)>();
        try
        {
            var metadata = _files.ContainsKey("pak/models.tmp") ? await ReadMetadataAsync("pak/models.tmp", token) : null;
            models = await ModelsPakArchive.LoadAsync(modelStream, metadata, ManagedGrnAssetLoader.Instance, token);
            Console.WriteLine($"[Browser] Model indexing completed in {indexing.Elapsed.TotalMilliseconds:F0} ms.");
            indexing.Restart();
            status("Indexing texture archive ranges…");
            foreach (var path in _files.Keys.Where(path => path.StartsWith("pak/texture", StringComparison.OrdinalIgnoreCase)
                         && path.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) && path.Count(c => c == '/') == 1))
                textureStreams.Add((Path.GetFileName(path), Open(path)));
            textures = await TexturePakArchive.LoadAsync(textureStreams, token);
            Console.WriteLine($"[Browser] Texture indexing completed in {indexing.Elapsed.TotalMilliseconds:F0} ms.");
            Console.WriteLine($"[Browser] Installation ready: {items.Count} items; {textures.Entries.Count} textures. Archives remain local Blob ranges.");
            return new AssetViewerSession(_directory, data, models, textures);
        }
        catch
        {
            if (models is null) modelStream.Dispose(); else models.Dispose();
            if (textures is null) foreach (var file in textureStreams) file.Stream.Dispose(); else textures.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        BrowserInterop.ReleaseInstallation(_manifest.Id);
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}

internal sealed record InstallationManifest(string Id, InstallationFile[] Entries);
internal sealed record InstallationFile(int Id, string Path, long Length);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(InstallationManifest))]
internal partial class InstallationJsonContext : JsonSerializerContext;
