namespace Sacred.Core.World.Sector;

public sealed class TileLayer(int width, int height)
{
    private readonly uint[] _tiles = new uint[width * height];

    public int Width { get; } = width;
    public int Height { get; } = height;

    public uint this[int x, int y]
    {
        get => _tiles[y * Width + x];
        set => _tiles[y * Width + x] = value;
    }
}