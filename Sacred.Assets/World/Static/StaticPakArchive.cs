using Sacred.Assets.Utils;
using Sacred.Core.World;

namespace Sacred.Assets.World.Static;

public sealed class StaticPakArchive : IDisposable
{
    private readonly MappedPakRecordTable<StaticObjectRecord> _records;

    private StaticPakArchive(MappedPakRecordTable<StaticObjectRecord> records) => _records = records;

    public static StaticPakArchive Load(string path)
    {
        using var stopwatch = new LoggingStopwatch("Loading Static.pak... ");

        return new StaticPakArchive(new MappedPakRecordTable<StaticObjectRecord>(
            path,
            StaticObjectRecord.SerializedSize,
            "Static.pak"));
    }

    public StaticObjectRecord? Get(uint staticId) => _records.Get(staticId);

    public void Dispose() => _records.Dispose();
}
