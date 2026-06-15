using Sacred.Assets.Utils;
using Sacred.Core.World;

namespace Sacred.Assets.World.Floor;

public sealed class FloorPakArchive
{
    private readonly FloorPakData _data;

    private FloorPakArchive(FloorPakData data) => _data = data;

    public static FloorPakArchive Load(string path)
    {
        using var stopwatch = new LoggingStopwatch("Loading Floor.pak... ");

        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Floor PAK path cannot be empty.", nameof(path));

        var floorPakData = FloorPakData.FromBytes(File.ReadAllBytes(path));
        return new FloorPakArchive(floorPakData);
    }

    public FloorOverlayRecord? Get(uint floorId) => _data.Get(floorId);
}
