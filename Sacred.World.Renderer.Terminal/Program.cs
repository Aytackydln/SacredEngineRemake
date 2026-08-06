using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Sacred.Assets.Paks.Items;
using Sacred.Assets.Paks.Mixed;
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
        worldCenter, options.Width, options.Height, options.Zoom);
    Write("world-day.bmp", dayWorld.Image);
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
