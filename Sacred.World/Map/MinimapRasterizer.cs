using System.Numerics;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.World.Sector;
using Sacred.World.Rendering;

namespace Sacred.World.Map;

public sealed class MinimapRasterizer(SacredWorldArchive world, TexturePakArchive textures)
{
    private const int SectorRadius = 3;
    private const float MapBrightness = 0.72f;

    public async Task<RgbaImage> RenderAsync(
        Vector2 playerWorldPosition,
        int width = 512,
        int height = 512,
        CancellationToken cancellationToken = default)
    {
        var canvas = new RgbaCanvas(width, height, 27, 23, 17);
        var centerSector = new SectorCoord(
            (int)MathF.Floor(playerWorldPosition.X / Sector.TileCount),
            (int)MathF.Floor(playerWorldPosition.Y / Sector.TileCount));
        var playerOffset = playerWorldPosition - new Vector2(
            (centerSector.X + 0.5f) * Sector.TileCount,
            (centerSector.Y + 0.5f) * Sector.TileCount);

        var loads = new List<Task<MinimapTile?>>();
        for (var deltaY = -SectorRadius; deltaY <= SectorRadius; deltaY++)
        for (var deltaX = -SectorRadius; deltaX <= SectorRadius; deltaX++)
            loads.Add(LoadAsync(new SectorCoord(centerSector.X + deltaX, centerSector.Y + deltaY), cancellationToken));

        foreach (var tile in await Task.WhenAll(loads).ConfigureAwait(false))
        {
            if (tile is null)
                continue;

            var deltaX = tile.Coord.X - centerSector.X;
            var deltaY = tile.Coord.Y - centerSector.Y;
            var playerOffsetX = (playerOffset.X - playerOffset.Y) * tile.Texture.Width / Sector.TileCount;
            var playerOffsetY = (playerOffset.X + playerOffset.Y) * tile.Texture.Height / (Sector.TileCount * 2.0f);
            var drawX = width * 0.5f - tile.Texture.Width +
                        (deltaX - deltaY) * tile.Texture.Width - playerOffsetX;
            var drawY = height * 0.5f - tile.Texture.Height * 0.5f +
                        (deltaX + deltaY) * tile.Texture.Height * 0.5f - playerOffsetY;
            canvas.DrawTexture(tile.Texture, drawX, drawY, tile.Texture.Width, tile.Texture.Height, MapBrightness);
        }

        canvas.DrawCross(width / 2, height / 2, 9, 255, 244, 165);
        return canvas.ToImage();
    }

    private async Task<MinimapTile?> LoadAsync(SectorCoord coord, CancellationToken cancellationToken)
    {
        if (!world.TryGetMinimapTextureName(coord, out var name))
            return null;
        try
        {
            return new MinimapTile(coord, await textures.LoadTextureAsync(name, cancellationToken).ConfigureAwait(false));
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private sealed record MinimapTile(SectorCoord Coord, TextureAsset Texture);
}
