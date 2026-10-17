namespace Sacred.Network.Gameplay;

/// <summary>Session-local ordered 0x153 reservations. The host, hero and item producers must
/// consume one shared pool; a reservation is never fabricated or returned after use.</summary>
public sealed class GoldObjectReferencePool
{
    private readonly Queue<uint> _available = [];
    private readonly HashSet<uint> _issued = [];
    public int Available => _available.Count;
    public int Issued => _issued.Count;

    public void Apply(GoldReferenceBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var pending = new HashSet<uint>();
        foreach (var reference in batch.References)
            if (_issued.Contains(reference) || !pending.Add(reference))
                throw new InvalidDataException($"Repeated object reservation {reference}; the session's ownership is unresolved.");
        foreach (var reference in batch.References)
        {
            _issued.Add(reference);
            _available.Enqueue(reference);
        }
    }

    public bool TryAllocate(out uint reference) => _available.TryDequeue(out reference);
    public void Clear() { _available.Clear(); _issued.Clear(); }
}
