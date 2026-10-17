using System.Collections.ObjectModel;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Known writes into the native +0x7684 block. Untouched words stay unknown;
/// a short command overwrites its prefix instead of resetting the entire block.</summary>
public sealed class WorldScriptStartupParameters
{
    private readonly Dictionary<int, int> _words = [];
    public WorldScriptStartupParameters() => Words = new ReadOnlyDictionary<int, int>(_words);
    public IReadOnlyDictionary<int, int> Words { get; }
    internal void Set(IReadOnlyList<int> values)
    {
        for (var i = 0; i < values.Count; i++) _words[i] = values[i];
    }
}
