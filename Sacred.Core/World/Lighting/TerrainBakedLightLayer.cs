namespace Sacred.Core.World.Lighting;

/// <summary>Stores the four authored WLDX baked-light samples for every terrain tile.</summary>
public sealed class TerrainBakedLightLayer
{
    private readonly TerrainBakedLightTile[] _tiles;

    public TerrainBakedLightLayer(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        _tiles = new TerrainBakedLightTile[checked(width * height)];
    }

    public int Width { get; }
    public int Height { get; }

    public TerrainBakedLightTile this[int x, int y]
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
