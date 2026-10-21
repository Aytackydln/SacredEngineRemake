using System.Collections.ObjectModel;

namespace Sacred.Network.Gameplay;

public sealed record GoldAttachedFxRequest(long Generation, long WearerInstance, GoldAttachedFxEvent Event);

/// <summary>Client-local creation requests bound to the wearer's lifetime on receipt.</summary>
public sealed class GoldAttachedFxReplica
{
    private readonly Dictionary<long, GoldAttachedFxRequest> _entries = [];
    private long _generation;
    public GoldAttachedFxReplica() => Entries = new ReadOnlyDictionary<long, GoldAttachedFxRequest>(_entries);
    public IReadOnlyDictionary<long, GoldAttachedFxRequest> Entries { get; }

    internal bool Add(GoldAttachedFxEvent entry, IReadOnlyDictionary<uint, GoldReplicatedObject> objects)
    {
        // Native receiver requires a currently active cObject3D before allocating FX.
        if (!objects.TryGetValue(entry.WearerReference, out var wearer) ||
            wearer.Physical is null && wearer.Creature is null ||
            (wearer.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0) return false;
        var generation = ++_generation;
        _entries.Add(generation, new(generation, wearer.InstanceVersion, entry));
        // Bounded receive history. A renderer retains ongoing playback independently.
        if (_entries.Count > 1024) _entries.Remove(_entries.Keys.Min());
        return true;
    }

    internal void Prune(IReadOnlyDictionary<uint, GoldReplicatedObject> objects)
    {
        foreach (var (id, request) in _entries.ToArray())
            if (!objects.TryGetValue(request.Event.WearerReference, out var wearer) ||
                wearer.InstanceVersion != request.WearerInstance ||
                (wearer.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0)
                _entries.Remove(id);
    }

    public void Clear() => _entries.Clear();
}
