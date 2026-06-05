using System;
using System.IO;

namespace Sacred.Assets;

public sealed class TilesPakArchive
{
    private readonly TilesPakData _data;

    private TilesPakArchive(TilesPakData data) => _data = data;

    public static TilesPakArchive Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Tiles PAK path cannot be empty.", nameof(path));

        return new TilesPakArchive(TilesPakData.FromBytes(File.ReadAllBytes(path)));
    }

    public TileDefinition? Get(uint tileId) => _data.Get(tileId);
}
