using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

public readonly record struct GoldMovementCallbackReference(uint Value, bool IsHash);
public sealed record GoldMovementCallbackGroup(string Name, IReadOnlyList<GoldMovementCallbackReference> Functions);
public sealed record GoldMovementCallbackChange(GoldMovementCallbackEvent? Registration, IReadOnlyList<GoldMovementCallbackGroup>? Snapshot,
    GoldScriptFunctionSnapshot? Functions = null);

/// <summary>Native ASCII-folded named vectors. Incremental words are indices; bank words are function-name hashes.</summary>
public sealed class GoldMovementCallbackReplica
{
    private readonly List<GoldMovementCallbackGroup> _groups = [];
    private readonly Queue<GoldMovementCallbackChange> _changes = [];
    public GoldMovementCallbackReplica() => Groups = _groups.AsReadOnly();
    public IReadOnlyList<GoldMovementCallbackGroup> Groups { get; }
    public int PendingChangeCount => _changes.Count;
    public bool TryPeekChange(out GoldMovementCallbackChange? change) => _changes.TryPeek(out change);
    public void CompleteChange(GoldMovementCallbackChange change)
    {
        if (!_changes.TryPeek(out var first) || !ReferenceEquals(first, change))
            throw new InvalidOperationException("Callback completion requires the current queued change.");
        _changes.Dequeue();
    }
    internal void Apply(GoldMovementCallbackEvent entry)
    {
        var decoded = GoldMovementCallbackCodec.Decode(entry.Message);
        var index = _groups.FindIndex(row => GoldScriptNames.Equal(row.Name, decoded.Name));
        var reference = new GoldMovementCallbackReference(decoded.FunctionIndex, false);
        if (index < 0) _groups.Add(new(decoded.Name, Array.AsReadOnly(new[] { reference })));
        else
        {
            var group = _groups[index];
            _groups[index] = group with { Functions = Array.AsReadOnly<GoldMovementCallbackReference>([..group.Functions, reference]) };
        }
        _changes.Enqueue(new(decoded, null));
    }
    internal void Apply(GoldWorldBankSnapshot bank, GoldScriptFunctionReplica functions)
    {
        if (bank.Opcode != GoldWorldBankLayout.Bank2Opcode) return;
        var groups = new List<GoldMovementCallbackGroup>();
        foreach (var record in bank.Sections[0].Records)
        {
            var name = GoldScriptNames.Read(record.Prefix);
            // Retain raw hashes and duplicate tree entries. The script import resolves the first matching
            // vector for each native traversal entry, including its empty/oversized-vector cleanup.
            var references = Enumerable.Range(0, record.Count)
                .Select(i => new GoldMovementCallbackReference(BinaryPrimitives.ReadUInt32LittleEndian(record.GetElement(i)), true)).ToArray();
            groups.Add(new(name, Array.AsReadOnly(references)));
        }
        _groups.Clear(); _groups.AddRange(groups);
        // Unlike the definition vector, the native named callback tree is cleared even for an empty bank section.
        _changes.Enqueue(new(null, Array.AsReadOnly(groups.ToArray()), functions.Capture()));
    }
    internal void Clear() { _groups.Clear(); _changes.Clear(); }
}
