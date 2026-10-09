using System.Numerics;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.World.Sector;
using Sacred.World.Geometry;
using Sacred.World.Particles;

namespace Sacred.World.Rendering;

/// <summary>Warms up authored world emitters and composites their live atlas cells.</summary>
public sealed class WorldParticleRasterizer(
    SacredWorldArchive world,
    ITextureSource textures,
    WorldStaticSpriteProvider? staticSprites = null)
{
    public WorldSpriteOcclusionCache? SpriteOcclusionCache { get; init; }
    /// <summary>Allows offline exporters to composite onto their final frame without copying it.</summary>
    public bool RenderInPlace { get; set; }
    public async Task<RgbaImage> RenderAsync(RgbaImage image, Vector2 center, float zoom,
        float simulatedSeconds = 2, IndoorTileGroup? activeIndoorGroup = null)
    {
        if (!float.IsFinite(simulatedSeconds) || simulatedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(simulatedSeconds));
        var origin = new SectorCoord((int)MathF.Floor(center.X / Sector.TileCount),
                                    (int)MathF.Floor(center.Y / Sector.TileCount));
        var loads = new List<Task<Sector?>>();
        for (var y = -1; y <= 1; y++)
        for (var x = -1; x <= 1; x++)
            loads.Add(world.TryLoadSector(new SectorCoord(origin.X + x, origin.Y + y)));
        var sectors = (await Task.WhenAll(loads)).OfType<Sector>().ToArray();
        var visible = new VisibleWorld(origin, sectors, 0);
        var system = new WorldParticleSystem(world.ParticleScript);
        // Update clamps large deltas. Fixed steps allow emission and motion to evolve.
        system.Update(0, visible, activeIndoorGroup);
        if (system.ActiveEmitterCount == 0 || simulatedSeconds == 0)
        {
            Console.WriteLine($"Particles: simulated {simulatedSeconds:F2}s, {system.ActiveEmitterCount} emitters, 0 live particles.");
            return image;
        }
        var steps = (int)Math.Ceiling(simulatedSeconds * 60.0);
        for (var step = 0; step < steps; step++)
            system.Update(Math.Min(1f / 60, simulatedSeconds - step / 60f), visible, activeIndoorGroup);
        Console.WriteLine($"Particles: simulated {simulatedSeconds:F2}s, {system.ActiveEmitterCount} emitters, {system.Particles.Count} live particles.");
        if (system.Particles.Count == 0) return image;
        var result = RenderInPlace ? image : new RgbaImage(image.Width, image.Height, (byte[])image.Pixels.Clone());
        var transform = IsometricProjection.CreateScreenTransform(center, zoom, image.Width, image.Height);
        var occlusion = staticSprites is null || system.Particles.Count == 0 ? null
            : await (SpriteOcclusionCache?.GetAsync(staticSprites, sectors, center, image.Width, image.Height, zoom, activeIndoorGroup)
                     ?? WorldModelOcclusion.BuildAsync(staticSprites, sectors, center, image.Width, image.Height, zoom, activeIndoorGroup));
        var atlases = new Dictionary<string, TextureAsset?>(StringComparer.OrdinalIgnoreCase);
        foreach (var particle in system.Particles.OrderBy(p => p.PainterDepthKey ?? WorldPainterDepth.FromWorld(new Vector2(p.WorldX, p.WorldY)))
                     .ThenBy(p => p.DrawOrder))
        {
            if (!atlases.TryGetValue(particle.Sprite.TextureName, out var atlas))
            {
                try { atlas = await textures.LoadTextureAsync(particle.Sprite.TextureName); }
                catch (FileNotFoundException) { atlas = null; }
                atlases.Add(particle.Sprite.TextureName, atlas);
            }
            if (atlas is not null) Draw(result, atlas, particle, transform, occlusion);
        }
        return result;
    }

    private static void Draw(RgbaImage image, TextureAsset atlas, WorldParticle particle,
        WorldScreenTransform transform, WorldModelOcclusionMask? occlusion)
    {
        var columns = particle.Sprite.AtlasColumns;
        var rows = particle.Sprite.AtlasRows;
        if (columns <= 0 || rows <= 0 || atlas.Width % columns != 0 || atlas.Height % rows != 0) return;
        var width = particle.Size * transform.Zoom;
        var height = particle.RenderHeight * transform.Zoom;
        if (width <= 0 || height <= 0) return;
        var anchor = IsometricProjection.WorldToIso(particle.WorldX, particle.WorldY) + IsometricProjection.TileAnchorOffset;
        var screen = transform.ToScreen(anchor.X, anchor.Y - particle.Height);
        var cos = MathF.Cos(particle.Rotation);
        var sin = MathF.Sin(particle.Rotation);
        var extentX = (MathF.Abs(cos) * width + MathF.Abs(sin) * height) * .5f;
        var extentY = (MathF.Abs(sin) * width + MathF.Abs(cos) * height) * .5f;
        var cellWidth = atlas.Width / columns;
        var cellHeight = atlas.Height / rows;
        var cell = Math.Clamp(particle.AtlasCell, 0, columns * rows - 1);
        var order = WorldStaticDrawOrder.TileKey((int)MathF.Floor(particle.WorldX), (int)MathF.Floor(particle.WorldY), ushort.MaxValue);
        var depth = particle.PainterDepthKey ?? WorldPainterDepth.FromWorld(new Vector2(particle.WorldX, particle.WorldY));
        for (var y = Math.Max(0, (int)MathF.Floor(screen.Y - extentY)); y < Math.Min(image.Height, screen.Y + extentY); y++)
        for (var x = Math.Max(0, (int)MathF.Floor(screen.X - extentX)); x < Math.Min(image.Width, screen.X + extentX); x++)
        {
            var dx = x + .5f - screen.X;
            var dy = y + .5f - screen.Y;
            var u = (cos * dx + sin * dy) / width + .5f;
            var v = (-sin * dx + cos * dy) / height + .5f;
            if (u < 0 || u >= 1 || v < 0 || v >= 1) continue;
            var pixel = y * image.Width + x;
            if (occlusion is not null && (occlusion.PainterOrder[pixel] > order || occlusion.SurfaceDepths[pixel] > depth)) continue;
            var source = ((cell / columns * cellHeight + (int)(v * cellHeight)) * atlas.Width +
                          cell % columns * cellWidth + (int)(u * cellWidth)) * 4;
            var alpha = atlas.Rgba8[source + 3] / 255f * particle.Opacity;
            // ONE-source draws retain RGB even when their authored diffuse alpha is zero.
            if (alpha < 1f / 255 && !particle.SourceColorOnly) continue;
            var sourceScale = particle.SourceColorOnly ? 1f : alpha;
            var targetScale = particle.Additive ? 1f : 1f - alpha;
            for (var channel = 0; channel < 3; channel++)
            {
                var tint = (particle.Color >> (16 - channel * 8)) & 255;
                image.Pixels[pixel * 4 + channel] = (byte)Math.Clamp(
                    atlas.Rgba8[source + channel] * tint / 255f * sourceScale + image.Pixels[pixel * 4 + channel] * targetScale, 0, 255);
            }
        }
    }
}
