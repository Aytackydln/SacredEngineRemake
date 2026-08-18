namespace Sacred.Core.World.Sector;

/// <summary>Marks cells which have an authored record in a sparse rectangular indoor grid.</summary>
public sealed class IndoorTilePresenceLayer(int width, int height)
{
    private readonly ulong[] _bits = new ulong[(checked(width * height) + 63) / 64];

    public int Width { get; } = width;
    public int Height { get; } = height;

    public bool this[int x, int y]
    {
        get
        {
            var index = IndexOf(x, y);
            return (_bits[index >> 6] & (1UL << (index & 63))) != 0;
        }
        set
        {
            var index = IndexOf(x, y);
            var mask = 1UL << (index & 63);
            if (value)
                _bits[index >> 6] |= mask;
            else
                _bits[index >> 6] &= ~mask;
        }
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
