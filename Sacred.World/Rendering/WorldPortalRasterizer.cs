using System.Numerics;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.World.Portals;
using Sacred.Core.World.Sector;
using Sacred.Particles;
using Sacred.World.Geometry;

namespace Sacred.World.Rendering;

/// <summary>Software counterpart of the unlit portal billboard shaders for terminal rendering.</summary>
public sealed class WorldPortalRasterizer(SacredWorldArchive world, ITextureSource textures,
    WorldStaticSpriteProvider staticSprites)
{
    public WorldSpriteOcclusionCache? SpriteOcclusionCache { get; init; }

    public async Task<RgbaImage> RenderAsync(RgbaImage image, Vector2 center, float zoom,
        float seconds, IndoorTileGroup? floor = null)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (floor is not null) return image;
        var origin = new SectorCoord((int)MathF.Floor(center.X / Sector.TileCount),
            (int)MathF.Floor(center.Y / Sector.TileCount));
        var placements = world.Portals.Billboards.Where(p => p.SurfaceLevel == 0 &&
            Math.Abs(p.TileX / Sector.TileCount - origin.X) <= 1 &&
            Math.Abs(p.TileY / Sector.TileCount - origin.Y) <= 1).ToArray();
        if (placements.Length == 0) return image;
        var loads = new List<Task<Sector?>>();
        for (var y = -1; y <= 1; y++)
        for (var x = -1; x <= 1; x++) loads.Add(world.TryLoadSector(new(origin.X + x, origin.Y + y)));
        var sectors = (await Task.WhenAll(loads)).OfType<Sector>().ToArray();
        var occlusion = await (SpriteOcclusionCache?.GetAsync(staticSprites, sectors, center, image.Width, image.Height, zoom, floor)
            ?? WorldModelOcclusion.BuildAsync(staticSprites, sectors, center, image.Width, image.Height, zoom, floor));
        var transform = IsometricProjection.CreateScreenTransform(center, zoom, image.Width, image.Height);
        var projection = SacredParticleCatalogue.LoadEmbedded().Projection;
        var whirlOne = await textures.LoadTextureAsync(SacredPortalAppearance.FirstWhirlTexture);
        var whirlTwo = await textures.LoadTextureAsync(SacredPortalAppearance.SecondWhirlTexture);
        foreach (var portal in placements.OrderBy(p => WorldPainterDepth.FromWorld(p.Position)))
        {
            var cycle = (uint)(seconds / 5);
            var first = await textures.LoadTextureAsync(SacredPortalAppearance.SurfaceTextures[TextureIndex((uint)portal.ScriptOffset, cycle)]);
            var second = await textures.LoadTextureAsync(SacredPortalAppearance.SurfaceTextures[TextureIndex((uint)portal.ScriptOffset, cycle + 1)]);
            var anchor = IsometricProjection.WorldToIso(portal.Position) + IsometricProjection.TileAnchorOffset;
            var heightScale = projection.HeightFactor * projection.VerticalScale;
            var screen = transform.ToScreen(anchor.X,
                anchor.Y - (SacredPortalAppearance.CenterHeight + portal.HeightOffset) * heightScale);
            var order = WorldStaticDrawOrder.TileKey(portal.TileX, portal.TileY, ushort.MaxValue);
            var depth = WorldPainterDepth.FromWorld(portal.Position);
            Draw(SacredPortalAppearance.SurfaceWidth * projection.HorizontalScale,
                SacredPortalAppearance.SurfaceHeight * heightScale, 0, false,
                uv => Surface(first, second, uv, seconds));
            Draw(2 * SacredPortalAppearance.WhirlHalfWidth * projection.HorizontalScale,
                2 * SacredPortalAppearance.WhirlHalfHeight * heightScale, seconds * .3f, true,
                uv => Sample(whirlOne, uv) * new Vector4(1, 1, 1, SacredPortalAppearance.FirstWhirlOpacity));
            Draw(2 * SacredPortalAppearance.WhirlHalfWidth * projection.HorizontalScale,
                2 * SacredPortalAppearance.WhirlHalfHeight * heightScale, seconds * .4f, true,
                uv => Sample(whirlTwo, uv) * new Vector4(1, 1, 1, SacredPortalAppearance.SecondWhirlOpacity), true);

            void Draw(float width, float height, float angle, bool whirl, Func<Vector2, Vector4> sample, bool additive = false)
            {
                width *= transform.Zoom;
                height *= transform.Zoom;
                var cosine = MathF.Cos(angle);
                var sine = MathF.Sin(angle);
                for (var y = Math.Max(0, (int)(screen.Y - height / 2)); y < Math.Min(image.Height, screen.Y + height / 2); y++)
                for (var x = Math.Max(0, (int)(screen.X - width / 2)); x < Math.Min(image.Width, screen.X + width / 2); x++)
                {
                    var local = new Vector2((x + .5f - screen.X) / width, (y + .5f - screen.Y) / height);
                    if (whirl) local = new Vector2(cosine * local.X + sine * local.Y,
                        -sine * local.X + cosine * local.Y) / .7071067812f;
                    var uv = local + new Vector2(.5f);
                    if (uv.X < 0 || uv.X >= 1 || uv.Y < 0 || uv.Y >= 1) continue;
                    var pixel = y * image.Width + x;
                    if (occlusion.PainterOrder[pixel] > order || occlusion.SurfaceDepths[pixel] > depth) continue;
                    var source = sample(uv);
                    if (source.W < 1f / 255) continue;
                    for (var channel = 0; channel < 3; channel++)
                        image.Pixels[pixel * 4 + channel] = (byte)Math.Clamp(
                            source[channel] * 255 * source.W + image.Pixels[pixel * 4 + channel] * (additive ? 1 : 1 - source.W), 0, 255);
                }
            }
        }
        Console.WriteLine($"Portals: {placements.Length} unlit animated billboards at {seconds:F2}s.");
        return image;
    }

    private static int TextureIndex(uint seed, uint cycle)
    {
        unchecked
        {
            var value = seed + cycle * 0x9E3779B9u;
            value = (value ^ value >> 16) * 0x7FEB352Du;
            value = (value ^ value >> 15) * 0x846CA68Bu;
            return (int)((value ^ value >> 16) % SacredPortalAppearance.SurfaceTextures.Count);
        }
    }

    private static Vector4 Surface(TextureAsset first, TextureAsset second, Vector2 uv, float seconds)
    {
        var vertexColour = SacredPortalSurfaceAnimation.SampleColour(uv, seconds);
        var firstAlpha = vertexColour.Y;
        var secondAlpha = vertexColour.Z;
        var coverage = secondAlpha + firstAlpha * (1 - secondAlpha);
        var sampleUv = new Vector2(uv.X * (19f / 33), uv.Y);
        var color = (Sample(second, sampleUv) * secondAlpha + Sample(first, sampleUv) * firstAlpha * (1 - secondAlpha)) /
            Math.Max(coverage, .0001f) * vertexColour.X;
        return new(color.X, color.Y, color.Z, coverage);
    }

    private static Vector4 Sample(TextureAsset texture, Vector2 uv)
    {
        var x = Math.Clamp((int)(uv.X * texture.Width), 0, texture.Width - 1);
        var y = Math.Clamp((int)(uv.Y * texture.Height), 0, texture.Height - 1);
        var offset = (y * texture.Width + x) * 4;
        return new Vector4(texture.Rgba8[offset], texture.Rgba8[offset + 1],
            texture.Rgba8[offset + 2], texture.Rgba8[offset + 3]) / 255;
    }
}
