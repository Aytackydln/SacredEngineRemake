using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace Sacred.Network.Gameplay;

/// <summary>Interpreter +0x7510: group keys and ordered member words, also received in bank 3 section 0.</summary>
public sealed class GoldScriptGroupReplica
{
    private readonly Dictionary<uint, List<uint>> _groups = [];
    private readonly Dictionary<uint, IReadOnlyList<uint>> _views = [];
    public GoldScriptGroupReplica() => Groups = new ReadOnlyDictionary<uint, IReadOnlyList<uint>>(_views);
    public IReadOnlyDictionary<uint, IReadOnlyList<uint>> Groups { get; }

    internal void Apply(GoldScriptGroupEvent entry)
    {
        var decoded = GoldScriptGroupCodec.Decode(entry.Message);
        if (decoded.Group is not { } key || decoded.Member is not { } member) return;
        if (decoded.Action == 0)
        {
            if (!_groups.TryGetValue(key, out var members)) members = Add(key, []);
            if (!members.Contains(member)) members.Add(member);
        }
        else if (_groups.TryGetValue(key, out var members))
        {
            // Native removal resets an invalid vector before searching for the requested member.
            if (members.Count > 1000) { members.Clear(); return; }
            var index = members.IndexOf(member);
            if (index < 0) return;
            members[index] = members[^1]; members.RemoveAt(members.Count - 1);
        }
    }

    internal void Apply(GoldWorldBankSnapshot bank)
    {
        if (bank.Opcode != GoldWorldBankLayout.Bank3Opcode || bank.Sections[0].Records.Count == 0) return;
        Clear();
        foreach (var record in bank.Sections[0].Records)
        {
            // Native lookup returns the first equal key; bank insertion can retain inaccessible duplicates.
            if (_groups.ContainsKey(record.Key)) continue;
            var members = new List<uint>(record.Count);
            for (var i = 0; i < record.Count; i++) members.Add(BinaryPrimitives.ReadUInt32LittleEndian(record.GetElement(i)));
            Add(record.Key, members);
        }
    }

    private List<uint> Add(uint key, List<uint> members)
    {
        _groups.Add(key, members); _views.Add(key, members.AsReadOnly()); return members;
    }
    internal void Clear() { _groups.Clear(); _views.Clear(); }
}
