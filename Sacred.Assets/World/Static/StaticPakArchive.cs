using Sacred.Core.World;

namespace Sacred.Assets.World.Static;

public sealed class StaticPakArchive
{
    private readonly StaticPakData _data;

    private StaticPakArchive(StaticPakData data) => _data = data;

    public static StaticPakArchive Load(string path) =>
        new(StaticPakData.FromBytes(File.ReadAllBytes(path)));

    public StaticObjectRecord? Get(uint staticId) => _data.Get(staticId);
}
