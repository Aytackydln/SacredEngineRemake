namespace Sacred.Core.World.Sector;

/// <summary>Outdoor WLDX tile +0x04 static-chain heads. Gold 0x6354D0 follows this
/// chain at the offset-adjusted anchor to find the first indoor surface parent.</summary>
public sealed class TileStaticChainLayer(int width, int height)
{
    private readonly uint[] _heads = new uint[checked(width * height)];
    public uint this[int x, int y]
    {
        get => _heads[y * width + x];
        set => _heads[y * width + x] = value;
    }
}
