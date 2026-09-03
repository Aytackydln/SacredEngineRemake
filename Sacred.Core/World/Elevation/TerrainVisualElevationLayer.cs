namespace Sacred.Core.World.Elevation;

/// <summary>Visual terrain-vertex offsets from WLDX bytes 0x10-0x13.</summary>
public sealed class TerrainVisualElevationLayer
{
    private readonly TerrainVisualElevationTile[] _tiles;

    public TerrainVisualElevationLayer(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        _tiles = new TerrainVisualElevationTile[checked(width * height)];
    }

    public int Width { get; }
    public int Height { get; }

    public TerrainVisualElevationTile this[int x, int y]
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
