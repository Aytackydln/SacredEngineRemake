namespace Sacred.Core.World.Sector;

public sealed class FloorOverlayLayer(int width, int height)
{
    private readonly List<FloorOverlay>[] _overlays = [
        .. Enumerable.Range(0, width * height)
            .Select(_ => new List<FloorOverlay>())];

    public int Width { get; } = width;
    public int Height { get; } = height;
    public int Count { get; private set; }

    public IReadOnlyList<FloorOverlay> this[int x, int y] => _overlays[y * Width + x];

    public void Add(int x, int y, FloorOverlay overlay)
    {
        var index = y * Width + x;
        var overlays = _overlays[index];

        overlays.Add(overlay);
        Count++;
    }
}