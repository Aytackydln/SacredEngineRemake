using Sacred.Assets.World.Floor;
using Sacred.Assets.World.Static;
using Sacred.Core.World.Stairs;
using Sacred.World.Objects;
using Sacred.World.Particles;
using Sacred.World.Portals;

namespace Sacred.World;

/// <summary>Opens terrain for clients without loading or executing campaign scripts.</summary>
public static class SacredWorldArchiveFactory
{
    public static SacredWorldArchive LoadClient(string gameDirectory, SacredStairsMap stairsMap)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        ArgumentNullException.ThrowIfNull(stairsMap);
        var worldDirectory = Path.Combine(Path.GetFullPath(gameDirectory), "World");
        FloorPakArchive? floorPak = null;
        StaticPakArchive? staticPak = null;
        FileStream? wldxStream = null;
        try
        {
            floorPak = FloorPakArchive.Load(Path.Combine(worldDirectory, "Floor.pak"));
            staticPak = StaticPakArchive.Load(Path.Combine(worldDirectory, "Static.pak"));
            wldxStream = new FileStream(Path.Combine(worldDirectory, "sectors.wldx"), FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite, bufferSize: 1, FileOptions.Asynchronous | FileOptions.RandomAccess);
            var result = Create(File.ReadAllBytes(Path.Combine(worldDirectory, "sectors.keyx")),
                wldxStream, floorPak, staticPak, stairsMap);
            wldxStream = null;
            floorPak = null;
            staticPak = null;
            Console.WriteLine("Client world terrain loaded; campaign execution belongs to the gameplay host.");
            return result;
        }
        finally
        {
            wldxStream?.Dispose();
            floorPak?.Dispose();
            staticPak?.Dispose();
        }
    }

    public static SacredWorldArchive Create(byte[] keyxData, FileStream wldxStream,
        FloorPakArchive floorPak, StaticPakArchive staticPak, SacredStairsMap stairsMap,
        WorldParticleScriptIndex? particleScript = null, WorldObjectScriptIndex? objectScript = null,
        WorldPortalScriptIndex? portals = null, WorldCampaignInfo? campaignInfo = null)
    {
        var result = SacredWorldArchive.Create(keyxData, wldxStream, floorPak, staticPak,
            stairsMap, particleScript, objectScript);
        result.Portals = portals ?? WorldPortalScriptIndex.Empty;
        result.CampaignInfo = campaignInfo;
        return result;
    }
}
