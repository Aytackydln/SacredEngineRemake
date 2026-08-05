using Sacred.Assets.Utils;
using Sacred.Core.World;

namespace Sacred.Assets.World.Floor;

public sealed class FloorPakArchive : IDisposable
{
    private readonly MappedPakRecordTable<FloorOverlayRecord> _records;

    private FloorPakArchive(MappedPakRecordTable<FloorOverlayRecord> records) => _records = records;

    public static FloorPakArchive Load(string path)
    {
        using var stopwatch = new LoggingStopwatch("Loading Floor.pak... ");

        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Floor PAK path cannot be empty.", nameof(path));

        return new FloorPakArchive(new MappedPakRecordTable<FloorOverlayRecord>(
            path,
            recordSize: 0x10,
            "Floor.pak"));
    }

    public FloorOverlayRecord? Get(uint floorId) => _records.Get(floorId);

    public void Dispose() => _records.Dispose();
}
