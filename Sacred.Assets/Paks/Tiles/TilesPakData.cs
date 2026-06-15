using System.Text;

namespace Sacred.Assets.Paks.Tiles;

public sealed class TilesPakData
{
    private const int HeaderSize = 0x100;
    private static readonly Encoding NameEncoding = Encoding.ASCII;

    private readonly List<TileDefinition> _definitions = [];

    private TilesPakData(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException("tiles.pak is too small to contain a header.");

        var count = PakDataHelpers.ReadEntryCount(data, HeaderSize, PakDataHelpers.EntryDescriptorSize, "tiles.pak");
        var descriptors = PakDataHelpers.ReadEntryDescriptors(data, HeaderSize, count, "tiles.pak");
        for (var i = 0; i < count; i++)
        {
            var descriptor = descriptors[i];
            var offset = descriptor.Offset;
            var size = descriptor.Size;
            if (offset <= 0 || size <= 0 || offset > int.MaxValue || size > int.MaxValue)
            {
                _definitions.Add(TileDefinition.Empty);
                continue;
            }

            var recordOffset = (int)offset;
            var recordSize = (int)size;
            if (recordOffset + recordSize > data.Length)
            {
                _definitions.Add(TileDefinition.Empty);
                continue;
            }

            var fileName = PakDataHelpers.ReadCString(data, recordOffset, 0x20, NameEncoding);
            var tileNumber = recordSize >= 0x28 ? BitConverter.ToUInt32(data.Slice(recordOffset + 0x24, 4)) : 0;
            _definitions.Add(new TileDefinition(fileName, tileNumber));
        }
    }

    public static TilesPakData FromBytes(ReadOnlySpan<byte> data) => new(data);

    public TileDefinition? Get(uint tileId) =>
        tileId <= int.MaxValue && (int)tileId < _definitions.Count
            ? _definitions[(int)tileId]
            : null;
}
