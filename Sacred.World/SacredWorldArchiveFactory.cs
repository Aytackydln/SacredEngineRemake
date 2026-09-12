using Sacred.Assets.Paks.Items;
using Sacred.Assets.World.Floor;
using Sacred.Assets.World.Static;
using Sacred.Core.World.Stairs;
using Sacred.World.Objects;
using Sacred.World.Particles;

namespace Sacred.World;

/// <summary>Creates a world archive while keeping ownership of its game-file handles explicit.</summary>
public static class SacredWorldArchiveFactory
{
    public static SacredWorldArchive Load(string gameDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        var fullGameDirectory = Path.GetFullPath(gameDirectory);
        var worldDirectory = Path.Combine(fullGameDirectory, "World");

        FloorPakArchive? floorPak = null;
        StaticPakArchive? staticPak = null;
        FileStream? wldxStream = null;
        try
        {
            floorPak = FloorPakArchive.Load(Path.Combine(worldDirectory, "Floor.pak"));
            staticPak = StaticPakArchive.Load(Path.Combine(worldDirectory, "Static.pak"));
            wldxStream = OpenWldx(Path.Combine(worldDirectory, "sectors.wldx"));
            var result = Create(
                File.ReadAllBytes(Path.Combine(worldDirectory, "sectors.keyx")),
                wldxStream,
                floorPak,
                staticPak,
                SacredStairsMap.Load(
                    Path.Combine(fullGameDirectory, "bin", "treppe.bin"),
                    Path.Combine(fullGameDirectory, "bin", "NetScript", "DefPos.bin")),
                WorldParticleScriptIndex.Load(Path.Combine(fullGameDirectory, "bin", "sgf.bin")),
                WorldObjectScriptIndex.Load(
                    FindWorldObjectScriptSources(fullGameDirectory),
                    ItemsPakArchive.Load(Path.Combine(fullGameDirectory, "pak", "Items.pak"))));
            wldxStream = null;
            floorPak = null;
            staticPak = null;
            return result;
        }
        finally
        {
            wldxStream?.Dispose();
            floorPak?.Dispose();
            staticPak?.Dispose();
        }
    }

    public static SacredWorldArchive Create(
        byte[] keyxData,
        FileStream wldxStream,
        FloorPakArchive floorPak,
        StaticPakArchive staticPak,
        SacredStairsMap stairsMap,
        WorldParticleScriptIndex? particleScript = null, WorldObjectScriptIndex? objectScript = null) =>
        SacredWorldArchive.Create(keyxData, wldxStream, floorPak, staticPak, stairsMap, particleScript, objectScript);

    private static FileStream OpenWldx(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 1,
        FileOptions.Asynchronous | FileOptions.RandomAccess);

    private static IReadOnlyList<WorldObjectScriptSource> FindWorldObjectScriptSources(string gameDirectory)
    {
        var binDirectory = Path.Combine(gameDirectory, "bin");
        var sources = Directory.EnumerateFiles(binDirectory, "StartCode.bin", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new WorldObjectScriptSource(path, Path.Combine(Path.GetDirectoryName(path)!, "DefPos.bin")))
            .ToList();
        sources.Add(new WorldObjectScriptSource(
            Path.Combine(binDirectory, "sgf.bin"),
            Path.Combine(binDirectory, "NetScript", "DefPos.bin")));
        return sources;
    }
}
