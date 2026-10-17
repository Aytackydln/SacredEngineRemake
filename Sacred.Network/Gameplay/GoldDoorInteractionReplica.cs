namespace Sacred.Network.Gameplay;

/// <summary>Receive-side door dispatch through the cObject3D activation boundary. Requested state
/// is compared with the linked trigger, never cached as a replacement for authoritative state.
/// Supported plain triggers execute activation and motion changes; unresolved callbacks retain their work.</summary>
public sealed class GoldDoorInteractionReplica
{
    private readonly List<GoldDoorInteraction> _pending = [];
    public GoldDoorInteractionReplica() => Pending = _pending.AsReadOnly();
    public IReadOnlyList<GoldDoorInteraction> Pending { get; }
    public long ReceivedCount { get; private set; }
    public long MissingOrInactiveCount { get; private set; }
    public long AlreadyInStateCount { get; private set; }
    public long AppliedCount { get; private set; }

    public void Apply(GoldDoorEvent entry, Dictionary<uint, GoldReplicatedObject> objects,
        GoldWorldTriggerReplica triggers)
    {
        var before = _pending.Count;
        Apply(entry, objects, triggers.Records);
        if (_pending.Count == before || before != 0) return;
        var work = _pending[^1];
        if (work.Object.Physical is null || work.Trigger is null ||
            !triggers.TryActivateDoor(work.TriggerReference, entry.Open)) return;
        objects[entry.ObjectReference] = work.Object with
        {
            BaseState = work.Object.BaseState with { Flags = work.Object.BaseState.Flags | 0x02000000 },
            Physical = work.Object.Physical.WithAnimation(entry.Open ? 0xaaU : 0xabU),
            ActivationVersion = work.Object.ActivationVersion + 1,
            ActivationTriggerState = triggers.Records[work.TriggerReference].State
        };
        _pending.RemoveAt(_pending.Count - 1);
        AppliedCount++;
    }

    public void Apply(GoldDoorEvent entry, IReadOnlyDictionary<uint, GoldReplicatedObject> objects,
        IReadOnlyDictionary<uint, GoldWorldTriggerState> triggers)
    {
        ReceivedCount++;
        if (!objects.TryGetValue(entry.ObjectReference, out var obj) ||
            (obj.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0)
        {
            MissingOrInactiveCount++;
            return;
        }
        // An unavailable trigger/type is unresolved work, not an assumed closed door.
        GoldWorldTriggerState? trigger = triggers.TryGetValue(obj.BaseState.TriggerReference, out var state) ? state : null;
        if (trigger is { } known && ((known.State & GoldDoorLayout.OpenState) != 0) == entry.Open)
        {
            AlreadyInStateCount++;
            return;
        }
        _pending.Add(new(entry with { Message = entry.Message with { Payload = entry.Message.Payload.ToArray() },
            RemainingData = entry.RemainingData.ToArray() }, obj, trigger));
    }

    public void Clear()
    {
        _pending.Clear();
        ReceivedCount = MissingOrInactiveCount = AlreadyInStateCount = AppliedCount = 0;
    }
}
