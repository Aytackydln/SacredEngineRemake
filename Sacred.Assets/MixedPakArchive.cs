using System.Collections.Generic;
using System.IO;

namespace Sacred.Assets;

public sealed class MixedPakArchive
{
    private readonly MixedPakData _data;

    private MixedPakArchive(MixedPakData data) => _data = data;

    public static MixedPakArchive Load(string path) =>
        new(MixedPakData.FromBytes(File.ReadAllBytes(path)));

    public IReadOnlyList<MixedCutoutRecord>? GetGroup(uint groupId) => _data.GetGroup(groupId);

    public uint? ResolveGroupId(uint referenceId) => _data.ResolveGroupId(referenceId);
}
