using Sacred.Assets.Paks.Texture;

namespace Sacred.World.Map;

public sealed class WorldMapAtlasLoader(
    Func<string, CancellationToken, Task<TextureAsset>> loadTextureAsync)
{
    private const int TileRows = 8;
    private const int TileColumns = 8;
    private const string PlayerMarkerTextureName = "WORLDMAP_WAYPOINT.TGA";

    public async Task<WorldMapAtlas> LoadAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("Loading Ancaria world-map tiles.");
        var loads = new Task<TextureAsset>[TileRows * TileColumns];
        for (var row = 0; row < TileRows; row++)
        for (var column = 0; column < TileColumns; column++)
        {
            var textureName = $"UI_WORLDMAPX{row}{column}.TGA";
            loads[row * TileColumns + column] = loadTextureAsync(textureName, cancellationToken);
        }

        var markerLoad = loadTextureAsync(PlayerMarkerTextureName, cancellationToken);
        var tiles = await Task.WhenAll(loads).ConfigureAwait(false);
        var marker = await markerLoad.ConfigureAwait(false);
        var tileWidth = tiles[0].Width;
        var tileHeight = tiles[0].Height;
        var width = checked(tileWidth * TileColumns);
        var height = checked(tileHeight * TileRows);
        var rgba = new byte[checked(width * height * 4)];

        for (var row = 0; row < TileRows; row++)
        for (var column = 0; column < TileColumns; column++)
        {
            var tile = tiles[row * TileColumns + column];
            ValidateTile(tile, tileWidth, tileHeight);
            for (var y = 0; y < tileHeight; y++)
            {
                Buffer.BlockCopy(
                    tile.Rgba8,
                    y * tileWidth * 4,
                    rgba,
                    ((row * tileHeight + y) * width + column * tileWidth) * 4,
                    tileWidth * 4);
            }
        }

        Console.WriteLine($"Ancaria world map loaded: {TileColumns}x{TileRows} tiles, {width}x{height} pixels.");
        return new WorldMapAtlas(width, height, rgba, marker);
    }

    private static void ValidateTile(TextureAsset tile, int expectedWidth, int expectedHeight)
    {
        if (tile.Width != expectedWidth || tile.Height != expectedHeight)
        {
            throw new InvalidOperationException(
                $"World-map tile '{tile.Name}' is {tile.Width}x{tile.Height}; " +
                $"expected {expectedWidth}x{expectedHeight}.");
        }

        if (tile.Rgba8.Length != checked(tile.Width * tile.Height * 4))
            throw new InvalidOperationException($"World-map tile '{tile.Name}' has an invalid pixel count.");
    }
}
