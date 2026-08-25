namespace Sacred.Core.World.Sector;

public sealed class FloorOverlayLayer(int width, int height)
{
    private static readonly IReadOnlyList<FloorOverlay> Empty = [];
    private readonly List<FloorOverlay>?[] _overlays = new List<FloorOverlay>?[width * height];

    public int Width { get; } = width;
    public int Height { get; } = height;
    public int Count { get; private set; }

    public IReadOnlyList<FloorOverlay> this[int x, int y] => _overlays[y * Width + x] ?? Empty;

    public void Add(int x, int y, FloorOverlay overlay)
    {
        var index = y * Width + x;
        var overlays = _overlays[index] ??= [];

        overlays.Add(overlay);
        Count++;
    }
}
