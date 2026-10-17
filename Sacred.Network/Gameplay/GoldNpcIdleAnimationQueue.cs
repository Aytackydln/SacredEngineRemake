namespace Sacred.Network.Gameplay;

/// <summary>Ordered client effects retained until the shared world simulation consumes them.
/// Entries are not collapsed by initiator: each native receive consumes its own random decisions.</summary>
public sealed class GoldNpcIdleAnimationQueue
{
    private readonly Queue<GoldNpcIdleAnimationEvent> _pending = [];
    public int PendingCount => _pending.Count;
    public void Add(GoldNpcIdleAnimationEvent entry) => _pending.Enqueue(entry);
    public bool TryDequeue(out GoldNpcIdleAnimationEvent? entry) => _pending.TryDequeue(out entry);
    public void Clear() => _pending.Clear();
}
