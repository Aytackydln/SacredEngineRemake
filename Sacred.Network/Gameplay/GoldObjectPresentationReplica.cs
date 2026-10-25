namespace Sacred.Network.Gameplay;

public sealed record GoldObjectPresentationWork(GoldGameEvent Event, GoldReplicatedObject Object);

/// <summary>Native receipt guards and ordered virtual-call requests. Model and trigger execution must consume the work.</summary>
public sealed class GoldObjectPresentationReplica
{
    private readonly List<GoldObjectPresentationWork> _pending = [];
    public GoldObjectPresentationReplica() => Pending = _pending.AsReadOnly();
    public IReadOnlyList<GoldObjectPresentationWork> Pending { get; }
    internal event Action<GoldObjectPresentationWork>? Published;
    public long MissingOrInactiveCount { get; private set; }
    public long AlreadyClosedCount { get; private set; }
    internal bool Apply(GoldGameEvent entry, Dictionary<uint, GoldReplicatedObject> objects)
    {
        var decoded = GoldObjectPresentationCodec.Decode(entry.Message);
        var reference = decoded is GoldObjectAnimationEvent animation ? animation.ObjectReference : ((GoldContainerCloseEvent)decoded).ObjectReference;
        if (!objects.TryGetValue(reference, out var obj) || obj.Physical is null ||
            decoded is GoldContainerCloseEvent && obj.Container is null ||
            (obj.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0)
        { MissingOrInactiveCount++; return false; }
        if (decoded is GoldContainerCloseEvent && (obj.BaseState.Flags & GoldContainerActivationCodec.ActivatedFlag) == 0)
        { AlreadyClosedCount++; return false; }
        // 170's receiver sets 0x02000000 even if virtual CreateAnimation fails. The requested motion
        // must not be installed as successful before the original resource/model lookup has run.
        // 18D clears its guard before dispatching activation event 0x21; that handler may set it again.
        var flags = decoded is GoldObjectAnimationEvent ? obj.BaseState.Flags | GoldObjectPresentationCodec.AnimationChangedFlag :
            obj.BaseState.Flags & ~GoldContainerActivationCodec.ActivatedFlag;
        var changed = obj with { BaseState = obj.BaseState with { Flags = flags } };
        objects[reference] = changed;
        _pending.Add(new(decoded, changed));
        Published?.Invoke(_pending[^1]);
        return true;
    }
    internal void Clear() { _pending.Clear(); MissingOrInactiveCount = AlreadyClosedCount = 0; }
    internal static bool Complete(GoldObjectPresentationWork work, uint motion, Dictionary<uint, GoldReplicatedObject> objects)
    {
        var reference = work.Object.BaseState.ObjectReference;
        if (!objects.TryGetValue(reference, out var current) || current.InstanceVersion != work.Object.InstanceVersion ||
            current.Physical is null || (current.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0) return false;
        if (current.PositionVersion != work.Object.PositionVersion && current.PositionOpcode is >= 0x169 and <= 0x16E) return false;
        var flags = current.BaseState.Flags | GoldObjectPresentationCodec.AnimationChangedFlag;
        if (work.Event is GoldContainerCloseEvent) flags |= GoldContainerActivationCodec.ActivatedFlag;
        objects[reference] = current with { Physical = current.Physical.WithAnimation(motion),
            BaseState = current.BaseState with { Flags = flags }, ActivationVersion = current.ActivationVersion + 1 };
        return true;
    }
}
