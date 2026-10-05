using Sacred.Assets.Paks.Items;
using Sacred.Assets.World.Floor;
using Sacred.Assets.World.Static;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.World.Stairs;
using Sacred.World.Objects;
using Sacred.World.Particles;
using Sacred.World.Portals;

namespace Sacred.World;

/// <summary>Creates a world archive while keeping ownership of its game-file handles explicit.</summary>
public static class SacredWorldArchiveFactory
{
    public static SacredWorldArchive Load(string gameDirectory) => Load(gameDirectory, null);

    public static SacredWorldArchive Load(string gameDirectory, string? campaignScriptsDirectory,
        string? stairsMapPath = null, string? defPosPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        var fullGameDirectory = Path.GetFullPath(gameDirectory);
        var worldDirectory = Path.Combine(fullGameDirectory, "World");
        var campaignFiles = SacredCampaignFiles.Resolve(fullGameDirectory, campaignScriptsDirectory);
        var campaign = WorldCampaignScripts.Load(campaignFiles);

        FloorPakArchive? floorPak = null;
        StaticPakArchive? staticPak = null;
        FileStream? wldxStream = null;
        try
        {
            floorPak = FloorPakArchive.Load(Path.Combine(worldDirectory, "Floor.pak"));
            staticPak = StaticPakArchive.Load(Path.Combine(worldDirectory, "Static.pak"));
            wldxStream = OpenWldx(Path.Combine(worldDirectory, "sectors.wldx"));
            var items = ItemsPakArchive.Load(Path.Combine(fullGameDirectory, "pak", "Items.pak")).ToArray();
            var portals = WorldPortalScriptIndex.Load(
                campaignFiles.DirectoryPath, items);
            var result = Create(
                File.ReadAllBytes(Path.Combine(worldDirectory, "sectors.keyx")),
                wldxStream,
                floorPak,
                staticPak,
                SacredStairsMap.Load(
                    stairsMapPath ?? Path.Combine(fullGameDirectory, "bin", "treppe.bin"),
                    defPosPath ?? campaignFiles.DefPosPath),
                WorldParticleScriptIndex.Load(Path.Combine(fullGameDirectory, "bin", "sgf.bin")),
                WorldObjectScriptIndex.Load(
                    FindWorldObjectScriptSources(fullGameDirectory, campaignFiles),
                    items));
            result.Portals = portals;
            result.CampaignScripts = campaign;
            Console.WriteLine($"Campaign loaded: {campaignFiles.Name}; {campaign.Functions.Count:N0} functions, " +
                $"{campaign.Positions.Count:N0} positions, {result.StairsMap.Links.Count:N0} stairs links.");
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
        FileShare.ReadWrite,
        bufferSize: 1,
        FileOptions.Asynchronous | FileOptions.RandomAccess);

    private static IReadOnlyList<WorldObjectScriptSource> FindWorldObjectScriptSources(
        string gameDirectory, SacredCampaignFiles campaign)
    {
        var binDirectory = Path.Combine(gameDirectory, "bin");
        return [new(campaign.StartCodePath, campaign.DefPosPath),
            new(Path.Combine(binDirectory, "sgf.bin"), campaign.DefPosPath)];
    }
}
