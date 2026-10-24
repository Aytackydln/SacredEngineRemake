using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>197 replies contain two fixed zero-padded reference arrays, never an echo of the request body.</summary>
public sealed class GoldReferenceReconciliationReplica
{
    public const int Capacity = 0x1FFFC;
    private uint[]? _nativeReferences;
    private readonly List<GoldMessage> _replies = [];
    public GoldReferenceReconciliationReplica() => Replies = _replies.AsReadOnly();
    public IReadOnlyList<GoldMessage> Replies { get; }
    public long PendingCount { get; private set; }
    /// <summary>7F5DF0's separate native reference vector; Objects does not reconstruct this allocation list.</summary>
    public void ConfigureReferences(IEnumerable<uint> references) => _nativeReferences = references.ToArray();
    internal void Apply(IReadOnlyDictionary<uint, GoldReplicatedObject> objects, byte local)
    {
        if (_nativeReferences is null) { PendingCount++; return; }
        var payload = new byte[Capacity * 8];
        if (_nativeReferences.Length < Capacity)
            for (var i = 0; i < _nativeReferences.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(i * 4), _nativeReferences[i]);
        var cursor = Capacity * 4;
        foreach (var (reference, obj) in objects.OrderBy(pair => pair.Key))
            if (reference is > 0 and < Capacity && (obj.BaseState.Flags & 0x10) == 0 && obj.BaseState.Owner == local)
            { BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(cursor), reference); cursor += 4; }
        _replies.Add(new(0x197, local, local, 0, payload));
    }
    internal void Clear() { _nativeReferences = null; _replies.Clear(); PendingCount = 0; }
}
