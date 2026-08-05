using System.Collections.Frozen;

namespace Sacred.Core.GameRes;

public sealed class GameResStore(FrozenDictionary<uint, string> strings)
{
    public static GameResStore Empty { get; } = new(FrozenDictionary<uint, string>.Empty);

    public FrozenDictionary<uint, string> Strings { get; } = strings;

    public string GetString(string resourceKey, string fallback)
    {
        var resourceId = SacredResourceHash.Compute(resourceKey);
        return Strings.GetValueOrDefault(resourceId, fallback);
    }
}
