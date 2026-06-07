using System.Collections.Frozen;

namespace Sacred.Core.GameRes;

public class GameResStore(FrozenDictionary<uint, string> strings, FrozenDictionary<string, uint> reverseIndexMap)
{
    public FrozenDictionary<uint, string> Strings { get;  } = strings;

    public FrozenDictionary<string, uint> ReverseIndexMap { get;  } = reverseIndexMap;
    
    // ReverseIndex string to Strings value
    public FrozenDictionary<string, string> TranslatedStrings { get; } = reverseIndexMap
        .ToFrozenDictionary(kv => kv.Key, kv =>
        {
            var baseString = kv.Key;
            var resId = kv.Value;
            return strings.GetValueOrDefault(resId, baseString);
        });

    public string TranslateString(string baseString)
    {
        if (ReverseIndexMap.TryGetValue(baseString, out var resId))
        {
            return Strings.GetValueOrDefault(resId, baseString);
        }

        return baseString;
    }
}