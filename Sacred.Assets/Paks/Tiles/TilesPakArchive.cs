using Sacred.Assets.Utils;

namespace Sacred.Assets.Paks.Tiles;

public sealed class TilesPakArchive
{
    private readonly TilesPakData _data;

    private TilesPakArchive(TilesPakData data) => _data = data;

    public static TilesPakArchive Load(string path)
    {
        using var stopwatch = new LoggingStopwatch("Loading Tiles.pak... ");
 
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Tiles PAK path cannot be empty.", nameof(path));

        var tilesPakData = TilesPakData.FromBytes(File.ReadAllBytes(path));
        return new TilesPakArchive(tilesPakData);
    }

    public TileDefinition? Get(uint tileId) => _data.Get(tileId);
}
