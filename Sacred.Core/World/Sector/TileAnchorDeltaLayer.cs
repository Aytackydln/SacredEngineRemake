namespace Sacred.Core.World.Sector;

/// <summary>Original WLDX tile bytes 0x1C/0x1D, retained for every outdoor tile.
/// Gold 0x63556E adds both signed deltas before testing the anchor tile's Indoor flag.</summary>
public sealed class TileAnchorDeltaLayer(int width, int height)
{
    private readonly (sbyte X, sbyte Y)[] _values = new (sbyte, sbyte)[checked(width * height)];
    public (sbyte X, sbyte Y) this[int x, int y]
    {
        get => _values[y * width + x];
        set => _values[y * width + x] = value;
    }
}
