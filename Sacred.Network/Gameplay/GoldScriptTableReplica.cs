using System.Collections.ObjectModel;

namespace Sacred.Network.Gameplay;

/// <summary>Independent native vectors replaced in arrival order, including empty replacements.</summary>
public sealed class GoldScriptTableReplica
{
    private readonly Dictionary<ushort, GoldScriptTableSnapshot> _tables = [];
    public GoldScriptTableReplica() => Tables = new ReadOnlyDictionary<ushort, GoldScriptTableSnapshot>(_tables);
    public IReadOnlyDictionary<ushort, GoldScriptTableSnapshot> Tables { get; }
    internal void Apply(GoldScriptTableEvent entry)
    {
        // Public event constructors cannot substitute conflicting decoded table metadata.
        var decoded = GoldScriptTableCodec.Decode(entry.Message);
        _tables[decoded.Message.Opcode] = decoded.Snapshot;
    }
    internal void Clear() => _tables.Clear();
}
