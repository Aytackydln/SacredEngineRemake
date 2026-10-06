using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.World;
using Sacred.Core.World.Sector;
using Sacred.World;
using Sacred.World.Map;
using Sacred.World.Renderer.Terminal;
using Sacred.World.Rendering;

RendererOptions options;
try
{
    options = RendererOptions.Parse(args);
}
catch (ShowHelpException)
{
    Console.WriteLine(RendererOptions.Help);
    return 0;
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    Console.Error.WriteLine(RendererOptions.Help);
    return 2;
}

using var logging = new RendererLog(options.Verbose);
try
{
    RendererLog.Info($"Loading Sacred world from: {options.GameDirectory}");
    var pakDirectory = Path.Combine(options.GameDirectory, "pak");
    using var textures = TexturePakArchive.LoadFromDirectory(pakDirectory);
    using var world = SacredWorldArchiveFactory.Load(options.GameDirectory, options.CampaignScriptsDirectory);
    using var renderer = new WorldRenderSession(pakDirectory, world, textures);
    Directory.CreateDirectory(options.OutputDirectory);
    if (options.AllSectors || options.SectorX.HasValue)
        return await SectorGenerator.RunAsync(options, world, textures, renderer);
    var defaultCenter = new Vector2(
        (world.StartSector.X + 0.5f) * Sector.TileCount,
        (world.StartSector.Y + 0.5f) * Sector.TileCount);
    var worldCenter = new Vector2(options.WorldX ?? defaultCenter.X, options.WorldY ?? defaultCenter.Y);
    var activeIndoorGroup = options.IndoorLevel is { } indoorLevel
        ? await FindIndoorGroupAsync(world, worldCenter, indoorLevel)
        : null;
    if (options.IndoorLevel is not null && activeIndoorGroup is null)
        throw new ArgumentException(
            $"No authored indoor floor level {options.IndoorLevel} contains world {worldCenter.X:F2},{worldCenter.Y:F2}.");
    if (activeIndoorGroup is not null)
        RendererLog.Info(
            $"Indoor floor: {activeIndoorGroup.Id}, level {activeIndoorGroup.SurfaceLevel}, " +
            $"origin {activeIndoorGroup.WorldX},{activeIndoorGroup.WorldY}.");
    RendererLog.Info($"Rendering at world {worldCenter.X.ToString("F2", CultureInfo.InvariantCulture)}, " +
                      $"{worldCenter.Y.ToString("F2", CultureInfo.InvariantCulture)} (day).");

    var stopwatch = Stopwatch.StartNew();
    var map = await new WorldMapRasterizer(textures).RenderAsync(worldCenter);
    Write("map", map);
    var minimap = await new MinimapRasterizer(world, textures).RenderAsync(worldCenter);
    Write("minimap", minimap);
    var result = await renderer.RenderAsync(worldCenter, options.Width, options.Height, options.Zoom, options, activeIndoorGroup);
    var dayWorld = result.Terrain;
    Write("world-day", dayWorld.Image);
    Write("world-models", result.Complete);
    RendererLog.Detail(
        $"World image: {dayWorld.LoadedSectors} sectors, {dayWorld.RenderedTiles}/{dayWorld.CandidateTiles} tiles " +
        $"rendered, {dayWorld.MissingTiles} missing; " +
        $"{dayWorld.LiquidRenderedTiles}/{dayWorld.LiquidCandidateTiles} liquid tiles rendered; " +
        $"{dayWorld.StaticRenderedObjects}/{dayWorld.StaticCandidateObjects} static objects rendered, " +
        $"{dayWorld.StaticMissingObjects} visible sprites missing.");
    RendererLog.Info($"Completed in {stopwatch.Elapsed.TotalSeconds:F2}s. Output: {options.OutputDirectory}");
    return 0;

    void Write(string fileName, RgbaImage image)
    {
        var path = Path.Combine(options.OutputDirectory, fileName + "." + ImageWriter.Extension(options.Format));
        ImageWriter.Write(path, image, options.Format);
        RendererLog.Info($"Wrote {image.Width}x{image.Height}: {path}");
    }
}

catch (Exception exception)
{
    Console.Error.WriteLine(options.Verbose ? exception.ToString() : exception.Message);
    return 1;
}

static async Task<IndoorTileGroup?> FindIndoorGroupAsync(
    SacredWorldArchive world,
    Vector2 center,
    byte surfaceLevel)
{
    var worldX = (int)MathF.Floor(center.X);
    var worldY = (int)MathF.Floor(center.Y);
    var origin = new SectorCoord(worldX / Sector.TileCount, worldY / Sector.TileCount);
    var loads = new List<Task<Sector?>>(9);
    for (var y = -1; y <= 1; y++)
    for (var x = -1; x <= 1; x++)
        loads.Add(world.TryLoadSector(new SectorCoord(origin.X + x, origin.Y + y)));

    var sectors = await Task.WhenAll(loads);
    var outdoor = sectors.FirstOrDefault(s => s?.Coord == origin);
    var anchor = outdoor?.IndoorAnchors?[worldX % Sector.TileCount, worldY % Sector.TileCount];
    return sectors
        .Where(static sector => sector is not null)
        .SelectMany(static sector => sector!.IndoorTileGroups.Groups)
        .DistinctBy(static group => group.Id)
        .Where(group => group.SurfaceLevel == surfaceLevel &&
                        group.TryGetLocalTile(worldX, worldY, out var localX, out var localY) &&
                        !group.Pathing.IsBlocked(localX, localY) &&
                          (surfaceLevel > 1 || !group.Pathing[localX, localY].IsEntranceBoundary &&
                              (anchor is { } parent && group.BuildingAnchor == parent &&
                                  (group.Presence[localX, localY] || outdoor!.Pathing[worldX % Sector.TileCount,
                                      worldY % Sector.TileCount].Properties.Behavior == WldxTileBehavior.MovementBlockerA) ||
                                  group.Presence[localX, localY] && (anchor is null || group.BuildingAnchor is null))))
        .OrderBy(group => group.Width * group.Height)
        .FirstOrDefault();
}
