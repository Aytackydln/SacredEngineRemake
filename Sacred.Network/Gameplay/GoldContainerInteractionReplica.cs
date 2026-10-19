namespace Sacred.Network.Gameplay;

/// <summary>Receive-side 0x7DF271 and cContainer activation. Loot is supplied by subsequent
/// server object messages; receiving an activation never runs the host's loot factory.</summary>
public sealed class GoldContainerInteractionReplica
{
    private readonly List<GoldContainerInteraction> _pending = [];
    public GoldContainerInteractionReplica() => Pending = _pending.AsReadOnly();
    public IReadOnlyList<GoldContainerInteraction> Pending { get; }
    public long ReceivedCount { get; private set; }
    public long MissingOrInactiveCount { get; private set; }
    public long AlreadyActivatedCount { get; private set; }
    public long AppliedCount { get; private set; }

    public void Apply(GoldContainerActivationEvent entry, Dictionary<uint, GoldReplicatedObject> objects,
        GoldWorldTriggerReplica triggers)
    {
        ReceivedCount++;
        if (!objects.TryGetValue(entry.ObjectReference, out var obj) || obj.Container is null ||
            (obj.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0)
        {
            MissingOrInactiveCount++;
            return;
        }
        // Origin is the high five-bit header identity used by the native receive loop.
        // Facing happens before the opened guard, including duplicate notifications.
        if (objects.TryGetValue(entry.Message.Origin, out var initiator) && initiator.Physical is not null &&
            (initiator.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) == 0)
        {
            var dx = unchecked(obj.BaseState.X - initiator.BaseState.X);
            var dy = unchecked(obj.BaseState.Y - initiator.BaseState.Y);
            if (dx != 0 || dy != 0)
                objects[entry.Message.Origin] = initiator with
                { FacingBits = GoldPhysicalFacing.FromDirection(new(dx, dy, 0)) };
        }
        if ((obj.BaseState.Flags & GoldContainerActivationCodec.ActivatedFlag) != 0)
        {
            AlreadyActivatedCount++;
            return;
        }
        // Native receive sets the one-shot guard before virtual activation, even if a callback is required.
        var activated = obj with { BaseState = obj.BaseState with
            { Flags = obj.BaseState.Flags | GoldContainerActivationCodec.ActivatedFlag } };
        objects[entry.ObjectReference] = activated;
        var reference = obj.BaseState.TriggerReference;
        GoldWorldTriggerState? trigger = triggers.Records.TryGetValue(reference, out var state) ? state : null;
        uint? motion = null;
        if (_pending.Count == 0 && reference == 0)
        {
            // No trigger: the ordinary first activation selects 0xAA. A pre-existing
            // 0xAA requires the model's reset-motion availability, which is not a wire field.
            if (obj.Physical?.Word64 != 0xaa) motion = 0xaa;
        }
        else if (_pending.Count == 0 && trigger is { } known)
        {
            var open = (known.State & 1) == 0;
            if (triggers.TryActivateDoor(reference, open)) motion = open ? 0xaaU : 0xabU;
        }
        if (motion is null || obj.Physical is null)
        {
            _pending.Add(new(entry with { Message = entry.Message with { Payload = entry.Message.Payload.ToArray() } }, activated, trigger));
            return;
        }
        objects[entry.ObjectReference] = activated with
        {
            BaseState = activated.BaseState with { Flags = activated.BaseState.Flags | 0x02000000 },
            Physical = obj.Physical.WithAnimation(motion.Value),
            ActivationVersion = obj.ActivationVersion + 1,
            ActivationTriggerState = reference != 0 ? triggers.Records[reference].State : null
        };
        AppliedCount++;
    }

    public void Clear()
    {
        _pending.Clear();
        ReceivedCount = MissingOrInactiveCount = AlreadyActivatedCount = AppliedCount = 0;
    }
}

/// <summary>Arrival-time instance and trigger retained for unsupported activation callbacks.</summary>
public sealed record GoldContainerInteraction(GoldContainerActivationEvent Event, GoldReplicatedObject Object,
    GoldWorldTriggerState? Trigger);
