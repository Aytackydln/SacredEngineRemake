using Sacred.Assets.Utils;

namespace Sacred.Assets.Paks.Mixed;

public sealed class MixedPakArchive
{
    private readonly MixedPakData _data;

    private MixedPakArchive(MixedPakData data) => _data = data;

    public static MixedPakArchive Load(string path)
    {
        using var stopwatch = new LoggingStopwatch("Loading Mixed.pak... ");

        var mixedPakData = MixedPakData.FromBytes(File.ReadAllBytes(path));
            
        return new MixedPakArchive(mixedPakData);
    }

    public IReadOnlyList<MixedCutoutRecord>? GetGroup(uint groupId) => _data.GetGroup(groupId);

    public uint? ResolveGroupId(uint referenceId) => _data.ResolveGroupId(referenceId);
}
