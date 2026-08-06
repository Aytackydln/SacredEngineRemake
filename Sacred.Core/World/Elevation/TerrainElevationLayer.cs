namespace Sacred.Core.World.Elevation;

/// <summary>
/// Stores all four mapped WLDX elevation samples independently for every tile,
/// preserving the serialized tile data without imposing cross-tile deduplication.
/// </summary>
public sealed class TerrainElevationLayer
{
    private readonly TerrainElevationTile[] _tiles;

    public TerrainElevationLayer(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        _tiles = new TerrainElevationTile[checked(width * height)];
    }

    public int Width { get; }
    public int Height { get; }

    public TerrainElevationTile this[int x, int y]
    {
        get
        {
            ValidateTile(x, y);
            return _tiles[y * Width + x];
        }
        set
        {
            ValidateTile(x, y);
            _tiles[y * Width + x] = value;
        }
    }

    private void ValidateTile(int x, int y)
    {
        if ((uint)x >= (uint)Width)
            throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)Height)
            throw new ArgumentOutOfRangeException(nameof(y));
    }
}
