using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Sacred.Assets.Paks.Items;
using Sacred.Assets.Paks.Mixed;
using Sacred.Assets.Paks.Models;
using Sacred.Assets.Paks.Texture;
using Sacred.Assets.Paks.Tiles;
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

try
{
    Console.WriteLine($"Loading Sacred world from: {options.GameDirectory}");
    var pakDirectory = Path.Combine(options.GameDirectory, "pak");
    using var textures = TexturePakArchive.LoadFromDirectory(pakDirectory);
    var tiles = TilesPakArchive.Load(Path.Combine(pakDirectory, "tiles.pak"));
    var items = ItemsPakArchive.Load(Path.Combine(pakDirectory, "Items.pak"))
        .ToDictionary(static item => item.ItemIndex);
    var mixed = MixedPakArchive.Load(Path.Combine(pakDirectory, "mixed.pak"));
    using var world = SacredWorldArchiveFactory.Load(options.GameDirectory);
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
        Console.WriteLine(
            $"Indoor floor: {activeIndoorGroup.Id}, level {activeIndoorGroup.SurfaceLevel}, " +
            $"origin {activeIndoorGroup.WorldX},{activeIndoorGroup.WorldY}.");
    Directory.CreateDirectory(options.OutputDirectory);
    Console.WriteLine($"Rendering at world {worldCenter.X.ToString("F2", CultureInfo.InvariantCulture)}, " +
                      $"{worldCenter.Y.ToString("F2", CultureInfo.InvariantCulture)} (day).");

    var stopwatch = Stopwatch.StartNew();
    var map = await new WorldMapRasterizer(textures).RenderAsync(worldCenter);
    Write("map.bmp", map);
    var minimap = await new MinimapRasterizer(world, textures).RenderAsync(worldCenter);
    Write("minimap.bmp", minimap);
    var staticSprites = new WorldStaticSpriteProvider(textures, mixed, items);
    var dayWorld = await new DayWorldRasterizer(world, textures, tiles, staticSprites).RenderAsync(
        worldCenter, options.Width, options.Height, options.Zoom, activeIndoorGroup: activeIndoorGroup);
    Write("world-day.bmp", dayWorld.Image);
    using var modelArchive = ModelsPakArchive.Load(
        Path.Combine(pakDirectory, "models.pak"), Path.Combine(pakDirectory, "Models.tmp"));
    var modelWorld = await new WorldModelRasterizer(world, items, modelArchive, textures)
        { StaticSprites = staticSprites }
        .RenderAsync(dayWorld.Image, worldCenter, options.Zoom, options.OpenDoors, activeIndoorGroup);
    Write("world-models.bmp", modelWorld);
    Console.WriteLine(
        $"World image: {dayWorld.LoadedSectors} sectors, {dayWorld.RenderedTiles}/{dayWorld.CandidateTiles} tiles " +
        $"rendered, {dayWorld.MissingTiles} missing; " +
        $"{dayWorld.LiquidRenderedTiles}/{dayWorld.LiquidCandidateTiles} liquid tiles rendered; " +
        $"{dayWorld.StaticRenderedObjects}/{dayWorld.StaticCandidateObjects} static objects rendered, " +
        $"{dayWorld.StaticMissingObjects} visible sprites missing.");
    Console.WriteLine($"Completed in {stopwatch.Elapsed.TotalSeconds:F2}s. Output: {options.OutputDirectory}");
    return 0;

    void Write(string fileName, RgbaImage image)
    {
        var path = Path.Combine(options.OutputDirectory, fileName);
        BmpWriter.Write(path, image);
        Console.WriteLine($"Wrote {image.Width}x{image.Height}: {path}");
    }
}

catch (Exception exception)
{
    Console.Error.WriteLine(exception);
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

    return (await Task.WhenAll(loads))
        .Where(static sector => sector is not null)
        .SelectMany(static sector => sector!.IndoorTileGroups.Groups)
        .DistinctBy(static group => group.Id)
        .Where(group => group.SurfaceLevel == surfaceLevel &&
                        group.TryGetAuthoredLocalTile(worldX, worldY, out _, out _))
        .OrderBy(group => group.Width * group.Height)
        .FirstOrDefault();
}
