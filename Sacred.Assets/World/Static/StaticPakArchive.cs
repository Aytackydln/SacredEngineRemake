using Sacred.Assets.Utils;
using Sacred.Core.World;

namespace Sacred.Assets.World.Static;

public sealed class StaticPakArchive
{
    private readonly StaticPakData _data;

    private StaticPakArchive(StaticPakData data) => _data = data;

    public static StaticPakArchive Load(string path)
    {
        using var stopwatch = new LoggingStopwatch("Loading Static.pak... ");

        var staticPakData = StaticPakData.FromBytes(File.ReadAllBytes(path));
        return new StaticPakArchive(staticPakData);
    }

    public StaticObjectRecord? Get(uint staticId) => _data.Get(staticId);
}
