using System.Buffers.Binary;
using System.Collections.ObjectModel;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.Network.Gameplay;

/// <summary>Indexed replacements in the campaign's existing function vector; an update never grows it.</summary>
public sealed class GoldScriptFunctionReplica
{
    private SacredScriptFunctionDefinition[] _definitions = [];
    private SacredScriptFunctionDefinition[] _initialDefinitions = [];
    private long _generation;
    private readonly Dictionary<uint, SacredScriptFunctionDefinition> _updates = [];
    private readonly Dictionary<uint, SacredScriptFunctionDefinition> _pending = [];
    public GoldScriptFunctionReplica() => Updates = new ReadOnlyDictionary<uint, SacredScriptFunctionDefinition>(_updates);
    public IReadOnlyList<SacredScriptFunctionDefinition> Definitions { get; private set; } = Array.Empty<SacredScriptFunctionDefinition>();
    /// <summary>Latest accepted replacement per slot, independent of the record's Number field.</summary>
    public IReadOnlyDictionary<uint, SacredScriptFunctionDefinition> Updates { get; }
    public bool IsConfigured { get; private set; }
    public int PendingCount => _pending.Count;
    public long Revision { get; private set; }
    /// <summary>Freezes the effective vector now; an unloaded campaign baseline resolves later using only updates already received.</summary>
    public GoldScriptFunctionSnapshot Capture()
    {
        if (IsConfigured)
        {
            var frozen = Array.AsReadOnly(_definitions.ToArray());
            return new(() => frozen, new Dictionary<uint, SacredScriptFunctionDefinition>());
        }
        var updates = new Dictionary<uint, SacredScriptFunctionDefinition>(_pending);
        var generation = _generation;
        return new(() => IsConfigured && generation == _generation ? _initialDefinitions : null, updates);
    }

    /// <summary>Loads the original Vectoren.bin function prefix once per session. Receipt during asynchronous
    /// campaign loading is resolved against this exact count, without inventing missing vector slots.</summary>
    public void Configure(ReadOnlySpan<byte> vectors)
    {
        if (IsConfigured) return;
        if (vectors.Length < 4) throw new InvalidDataException("Truncated Vectoren.bin function count.");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(vectors);
        if (4L + (long)count * SacredScriptFunctionLayout.SerializedSize > vectors.Length)
            throw new InvalidDataException("Truncated Vectoren.bin function table.");
        var definitions = new SacredScriptFunctionDefinition[checked((int)count)];
        for (var index = 0; index < definitions.Length; index++)
            definitions[index] = SacredScriptFunctionDefinition.FromPayload(vectors.Slice(4 + index * SacredScriptFunctionLayout.SerializedSize, SacredScriptFunctionLayout.SerializedSize));
        _definitions = definitions;
        _initialDefinitions = definitions.ToArray();
        Definitions = Array.AsReadOnly(_definitions);
        IsConfigured = true;
        foreach (var pair in _pending) Replace(pair.Key, pair.Value);
        _pending.Clear();
        Revision++;
    }
    internal void Apply(GoldScriptFunctionEvent entry)
    {
        // The owned wire message wins over metadata supplied to a public event constructor.
        var decoded = GoldScriptFunctionCodec.Decode(entry.Message);
        if (!IsConfigured) _pending[decoded.Index] = decoded.Definition;
        else Replace(decoded.Index, decoded.Definition);
    }
    private void Replace(uint index, SacredScriptFunctionDefinition definition)
    {
        if (index >= _definitions.Length) return;
        _definitions[index] = definition;
        _updates[index] = definition;
        Revision++;
    }
    internal void Clear()
    {
        _definitions = [];
        _initialDefinitions = [];
        _generation++;
        Definitions = Array.Empty<SacredScriptFunctionDefinition>();
        _updates.Clear(); _pending.Clear();
        IsConfigured = false;
        Revision++;
    }
}
