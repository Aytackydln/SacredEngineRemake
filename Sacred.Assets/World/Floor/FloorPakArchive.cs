namespace Sacred.Assets.World.Floor;

public sealed class FloorPakArchive
{
    private readonly FloorPakData _data;

    private FloorPakArchive(FloorPakData data) => _data = data;

    public static FloorPakArchive Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Floor PAK path cannot be empty.", nameof(path));

        return new FloorPakArchive(FloorPakData.FromBytes(File.ReadAllBytes(path)));
    }

    public FloorOverlayRecord? Get(uint floorId) => _data.Get(floorId);
}
