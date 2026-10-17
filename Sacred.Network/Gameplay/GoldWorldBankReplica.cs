using System.Collections.ObjectModel;

namespace Sacred.Network.Gameplay;

/// <summary>Last bank per transport identity and opcode; observation does not confer host startup approval.</summary>
public sealed class GoldWorldBankReplica
{
    private readonly Dictionary<(byte Sender, byte Origin, ushort Opcode), GoldWorldBankSnapshot> _states = [];
    private readonly Dictionary<ushort, GoldWorldBankSnapshot> _current = [];
    public GoldWorldBankReplica()
    {
        States = new ReadOnlyDictionary<(byte Sender, byte Origin, ushort Opcode), GoldWorldBankSnapshot>(_states);
        Current = new ReadOnlyDictionary<ushort, GoldWorldBankSnapshot>(_current);
    }
    public IReadOnlyDictionary<(byte Sender, byte Origin, ushort Opcode), GoldWorldBankSnapshot> States { get; }
    /// <summary>Native bank import replaces each global bank in arrival order. States additionally
    /// retains routing identities for diagnostics; an older origin must not win current-state lookup.</summary>
    public IReadOnlyDictionary<ushort, GoldWorldBankSnapshot> Current { get; }
    public bool IsComplete => _current.Count == 4;
    internal void Apply(GoldWorldBankEvent entry)
    {
        _states[(entry.Message.Sender, entry.Message.Origin, entry.Message.Opcode)] = entry.Snapshot;
        _current[entry.Message.Opcode] = entry.Snapshot;
    }
    internal void Clear() { _states.Clear(); _current.Clear(); }
}
