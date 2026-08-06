using System.Numerics;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.World.Sector;
using Sacred.World.Geometry;

namespace Sacred.World.Rendering;

internal sealed class WorldLiquidRasterizer(TexturePakArchive textures)
{
    private const int TileWidth = 96;
    private const int TileHeight = 48;
    private readonly Dictionary<byte, Task<TextureAsset?>> _textureLoads = [];

    public async Task<WorldLiquidRenderResult> RenderAsync(
        RgbaCanvas canvas,
        IReadOnlyList<Sector> sectors,
        Vector2 centerIso,
        int width,
        int height,
        float zoom)
    {
        var (draws, covers) = Build(sectors, centerIso, width, height, zoom);
        await Task.WhenAll(draws.Select(draw => GetTextureAsync(draw.StyleId))).ConfigureAwait(false);
        var rendered = 0;
        foreach (var draw in draws)
        {
            var texture = await GetTextureAsync(draw.StyleId).ConfigureAwait(false);
            if (texture is null)
                continue;
            canvas.DrawLiquidDiamond(
                texture,
                draw.Variant,
                draw.AlphaLeft,
                draw.AlphaTop,
                draw.AlphaRight,
                draw.AlphaBottom,
                draw.ScreenX,
                draw.ScreenY,
                TileWidth * zoom,
                TileHeight * zoom);
            rendered++;
        }
        return new WorldLiquidRenderResult(draws.Count, rendered, covers);
    }

    private static (List<LiquidDraw> Draws, List<WorldTerrainCover> Covers) Build(
        IReadOnlyList<Sector> sectors,
        Vector2 centerIso,
        int width,
        int height,
        float zoom)
    {
        var draws = new List<LiquidDraw>();
        var covers = new List<WorldTerrainCover>();
        foreach (var sector in sectors)
        {
            var insertionDepths = new byte[Sector.TileCount * Sector.TileCount];
            Array.Fill(insertionDepths, byte.MaxValue);
            var sectorOriginX = sector.Coord.X * Sector.TileCount;
            var sectorOriginY = sector.Coord.Y * Sector.TileCount;
            foreach (var liquid in sector.LiquidSurfaces.Surfaces)
            {
                insertionDepths[liquid.LocalY * Sector.TileCount + liquid.LocalX] = liquid.FloorInsertionDepth;
                var worldX = sectorOriginX + liquid.LocalX;
                var worldY = sectorOriginY + liquid.LocalY;
                var iso = IsometricProjection.WorldToIso(worldX, worldY);
                var screenX = width * 0.5f + (iso.X + 2.0f - centerIso.X) * zoom;
                var screenY = height * 0.5f + (iso.Y + 1.0f - centerIso.Y) * zoom;
                if (!IntersectsViewport(screenX, screenY, width, height, zoom))
                    continue;
                var multiplier = AlphaMultiplier(liquid.StyleId);
                draws.Add(new LiquidDraw(
                    worldX + worldY,
                    worldY,
                    screenX,
                    screenY,
                    liquid.StyleId,
                    (byte)((worldX & 3) | ((worldY & 3) << 2)),
                    Alpha(liquid.AlphaLeft, multiplier),
                    Alpha(liquid.AlphaTop, multiplier),
                    Alpha(liquid.AlphaRight, multiplier),
                    Alpha(liquid.AlphaBottom, multiplier)));
            }

            for (var localY = 0; localY < Sector.TileCount; localY++)
            for (var localX = 0; localX < Sector.TileCount; localX++)
            foreach (var floor in sector.FloorOverlays[localX, localY])
            {
                if (floor.ChainDepth < insertionDepths[localY * Sector.TileCount + localX])
                    continue;
                var worldX = sectorOriginX + localX;
                var worldY = sectorOriginY + localY;
                var iso = IsometricProjection.WorldToIso(worldX, worldY);
                var screenX = width * 0.5f + (iso.X - centerIso.X) * zoom;
                var screenY = height * 0.5f + (iso.Y - centerIso.Y) * zoom;
                covers.Add(new WorldTerrainCover(
                    worldX + worldY,
                    worldY,
                    floor.ChainDepth,
                    screenX,
                    screenY,
                    floor.PrimaryTileId,
                    floor.SecondaryTileId));
            }
        }
        draws.Sort(static (left, right) =>
        {
            var depth = left.Depth.CompareTo(right.Depth);
            return depth != 0 ? depth : left.WorldY.CompareTo(right.WorldY);
        });
        covers.Sort(static (left, right) =>
        {
            var depth = left.Depth.CompareTo(right.Depth);
            if (depth != 0)
                return depth;
            var worldY = left.WorldY.CompareTo(right.WorldY);
            return worldY != 0 ? worldY : left.ChainDepth.CompareTo(right.ChainDepth);
        });
        return (draws, covers);
    }

    private Task<TextureAsset?> GetTextureAsync(byte styleId)
    {
        lock (_textureLoads)
        {
            if (_textureLoads.TryGetValue(styleId, out var load))
                return load;
            load = LoadTextureAsync(styleId);
            _textureLoads.Add(styleId, load);
            return load;
        }
    }

    private async Task<TextureAsset?> LoadTextureAsync(byte styleId)
    {
        try
        {
            return await textures.LoadTextureAsync(TextureName(styleId)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool IntersectsViewport(float x, float y, int width, int height, float zoom) =>
        x < width && y < height && x + TileWidth * zoom > 0 && y + TileHeight * zoom > 0;

    private static string TextureName(byte styleId) => styleId switch
    {
        0 or 1 or 13 => "B_WATER00.TGA",
        2 => "C_WATER00.TGA",
        3 => "D_WATER00.TGA",
        4 => "A_LAVA00.TGA",
        5 => "B_LAVA00.TGA",
        6 => "C_LAVA00.TGA",
        7 => "A_SCHWEFEL00.TGA",
        8 => "D_LAVA00.TGA",
        9 => "E_WATER00.TGA",
        10 => "F_WATER00.TGA",
        11 => "G_WATER00.TGA",
        12 => "E_LAVA00.TGA",
        _ => "C_WATER00.TGA"
    };

    private static int AlphaMultiplier(byte styleId) => styleId switch
    {
        4 or 5 or 6 or 7 or 8 or 9 or 12 => -255,
        10 => -24,
        _ => -12
    };

    private static byte Alpha(sbyte value, int multiplier) =>
        (byte)Math.Clamp(value * multiplier, 0, 255);

    private readonly record struct LiquidDraw(
        int Depth,
        int WorldY,
        float ScreenX,
        float ScreenY,
        byte StyleId,
        byte Variant,
        byte AlphaLeft,
        byte AlphaTop,
        byte AlphaRight,
        byte AlphaBottom);
}

internal readonly record struct WorldTerrainCover(
    int Depth,
    int WorldY,
    int ChainDepth,
    float ScreenX,
    float ScreenY,
    uint PrimaryTileId,
    uint SecondaryTileId);

internal readonly record struct WorldLiquidRenderResult(
    int Candidates,
    int Rendered,
    IReadOnlyList<WorldTerrainCover> Covers);
