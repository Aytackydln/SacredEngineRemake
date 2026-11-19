namespace Sacred.Network.Gameplay;

/// <summary>Session-local ordered 0x153 reservations. The host, hero and item producers must
/// consume one shared pool; a reservation is never fabricated or returned after use.</summary>
public sealed class GoldObjectReferencePool
{
    private readonly Queue<uint> _available = [];
    private readonly HashSet<uint> _issued = [];
    public int Available => _available.Count;
    public int Issued => _issued.Count;
    /// <summary>True only for an actual received reservation already consumed
    /// by this lifetime. Saved-object construction reuses its prepared IDs.</summary>
    public bool IsAllocated(uint reference) => _issued.Contains(reference) && !_available.Contains(reference);

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
    /// <summary>Inspect a prospective character allocation without consuming it.
    /// The complete import validates before Consume commits this same prefix.</summary>
    public uint[] PeekAvailable(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (count > Available) throw new InvalidDataException($"Character import needs {count} actual object references; only {Available} are available.");
        return _available.Take(count).ToArray();
    }
    /// <summary>Consume an already validated saved-character plan in native grant order.
    /// Validation precedes mutation so an inconsistent plan cannot partly drain the pool.</summary>
    public void Consume(ReadOnlySpan<uint> references)
    {
        if (!_available.Take(references.Length).SequenceEqual(references.ToArray()))
            throw new InvalidDataException("Saved-character references differ from the available native reservation order.");
        foreach (var reference in references) _available.Dequeue();
    }
    public void Clear() { _available.Clear(); _issued.Clear(); }
}
