namespace Sacred.Core.World.Pathing;

/// <summary>
/// Compact random-access navigation grid that preserves the original WLDX bytes
/// for future pathfinding while keeping collision checks branch-light.
/// </summary>
public sealed class WorldPathingLayer
{
    private readonly WorldPathFlags[] _flags;
    private readonly byte[] _typesAndSurfaces;
    private readonly ulong[] _blockedBits;

    public WorldPathingLayer(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;
        var tileCount = checked(width * height);
        _flags = new WorldPathFlags[tileCount];
        _typesAndSurfaces = new byte[tileCount];
        _blockedBits = new ulong[(tileCount + 63) / 64];
    }

    public int Width { get; }
    public int Height { get; }

    public WorldPathTile this[int x, int y]
    {
        get
        {
            var index = IndexOf(x, y);
            return new WorldPathTile(_flags[index], _typesAndSurfaces[index]);
        }
        set
        {
            var index = IndexOf(x, y);
            _flags[index] = value.Flags;
            _typesAndSurfaces[index] = value.TypeAndSurface;

            var mask = 1UL << (index & 63);
            if (value.IsBlocked)
                _blockedBits[index >> 6] |= mask;
            else
                _blockedBits[index >> 6] &= ~mask;
        }
    }

    public bool IsBlocked(int x, int y)
    {
        var index = IndexOf(x, y);
        return (_blockedBits[index >> 6] & (1UL << (index & 63))) != 0;
    }

    private int IndexOf(int x, int y)
    {
        if ((uint)x >= (uint)Width)
            throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)Height)
            throw new ArgumentOutOfRangeException(nameof(y));
        return y * Width + x;
    }
}
