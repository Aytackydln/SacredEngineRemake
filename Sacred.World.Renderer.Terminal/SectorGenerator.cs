using System.Diagnostics;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.World.Sector;
using Sacred.World.Map;

namespace Sacred.World.Renderer.Terminal;

internal static class SectorGenerator
{
    public static async Task<int> RunAsync(RendererOptions options, SacredWorldArchive world,
        TexturePakArchive textures, WorldRenderSession renderer)
    {
        var coords = options.AllSectors ? world.SectorCoordinates
            : [new SectorCoord(options.SectorX!.Value, options.SectorY!.Value)];
        var stopwatch = Stopwatch.StartNew();
        var generated = 0;
        var missing = 0;
        var skipped = 0;
        var failed = 0;
        var lastProgress = TimeSpan.Zero;
        RendererLog.Info(options.AllSectors
            ? $"Generating minimap textures for {coords.Count} sectors (x{options.ResolutionScale}). Output: {options.OutputDirectory}"
            : $"Generating sector {options.SectorX},{options.SectorY} (x{options.ResolutionScale}). Output: {options.OutputDirectory}");
        foreach (var coord in coords)
        {
            if (!world.TryGetMinimapTextureName(coord, out var name))
                throw new ArgumentException($"Sector {coord.X},{coord.Y} is not present in sectors.keyx.");
            // Dimensions come from the packed original, even when unpacked overrides exist.
            if (!textures.TryResolveTextureRecord(name, out var original))
            {
                missing++;
                RendererLog.Detail($"No original sector texture: {name}.");
                if (!options.AllSectors) throw new FileNotFoundException($"Original texture '{name}' is missing.");
                continue;
            }
            var output = Path.Combine(options.OutputDirectory, Path.GetFileNameWithoutExtension(name) + "." + ImageWriter.Extension(options.Format));
            if (options.SkipExisting && File.Exists(output)) { skipped++; continue; }
            try
            {
                var outputWidth = checked(original.Width * options.ResolutionScale);
                var outputHeight = checked(original.Height * options.ResolutionScale);
                // Render above the output resolution so terrain, sprites, models and
                // particles all receive the same area filter in the completed frame.
                var renderSize = checked(Math.Max(outputWidth, outputHeight) * options.Supersampling);
                if ((long)renderSize * renderSize * 4 > int.MaxValue)
                    throw new ArgumentException("Supersampled sector dimensions exceed the RGBA image limit.");
                RendererLog.Detail($"Sector {coord.X},{coord.Y}: generating {outputWidth}x{outputHeight}, " +
                    $"rendering {renderSize}x{renderSize} with area filtering, source {original.Name}.");
                var view = SectorMinimapView.Create(coord, renderSize);
                var image = await renderer.RenderSectorAsync(view.Center, view.RenderWidth, view.RenderHeight,
                    view.Zoom, options, outputWidth, outputHeight);
                var temporary = output + ".tmp";
                try
                {
                    ImageWriter.Write(temporary, image, options.Format);
                    File.Move(temporary, output, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                generated++;
                RendererLog.Detail($"Wrote {output} ({generated} generated, {stopwatch.Elapsed.TotalSeconds:F1}s elapsed).");
            }
            catch (Exception exception) when (options.AllSectors)
            {
                failed++;
                Console.Error.WriteLine($"Sector {coord.X},{coord.Y} failed: " +
                    (options.Verbose ? exception.ToString() : exception.Message));
            }
            // Avoid retaining a world's worth of sectors, composed sprites and meshes.
            if ((generated + failed) % 8 == 0) renderer.ReleaseBatch();
            if (options.AllSectors && stopwatch.Elapsed - lastProgress >= TimeSpan.FromSeconds(10))
            {
                lastProgress = stopwatch.Elapsed;
                RendererLog.Info($"Progress: {generated + skipped + missing + failed}/{coords.Count} sectors, " +
                    $"{generated} generated, {failed} failed ({stopwatch.Elapsed.TotalSeconds:F0}s).");
            }
        }
        var statistics = renderer.TextureCacheStatistics;
        RendererLog.Info($"Completed in {stopwatch.Elapsed.TotalSeconds:F2}s: {generated} generated, {skipped} existing, {missing} without original texture, {failed} failed.");
        RendererLog.Detail($"Texture cache: {statistics.Hits} hits, {statistics.Misses} misses.");
        return failed == 0 ? 0 : 1;
    }
}
