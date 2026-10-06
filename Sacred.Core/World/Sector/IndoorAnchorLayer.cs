namespace Sacred.Core.World.Sector;

/// <summary>Outdoor WLDX 0x1C/0x1D parent anchors, valid when 0x1E has the Indoor flag.</summary>
public sealed class IndoorAnchorLayer(int width, int height)
{
    private readonly (int X, int Y)?[] _anchors = new (int X, int Y)?[checked(width * height)];
    public (int X, int Y)? this[int x, int y]
    {
        get => _anchors[y * width + x];
        set => _anchors[y * width + x] = value;
    }
}
