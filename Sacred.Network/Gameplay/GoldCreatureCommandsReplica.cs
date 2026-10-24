namespace Sacred.Network.Gameplay;

/// <summary>Native receipt-time inputs for downstream definition/value/hit/model/particle consumers.</summary>
public sealed record GoldCreatureCommandWork(GoldCreatureCommandEvent Event, GoldReplicatedObject? Subject, GoldReplicatedObject? Other = null)
{
    public IReadOnlyList<GoldHitNotification> HitNotifications { get; init; } = Array.Empty<GoldHitNotification>();
}

public sealed class GoldCreatureCommandsReplica
{
    private readonly List<GoldCreatureCommandWork> _requests = [];
    public GoldCreatureCommandsReplica() => Requests = _requests.AsReadOnly();
    public IReadOnlyList<GoldCreatureCommandWork> Requests { get; }
    public GoldCreatureInteractionsReplica Interactions { get; } = new();
    public GoldDialogRegistrationReplica Dialogs { get; } = new();
    public long ReceivedCount { get; private set; }
    public long RejectedCount { get; private set; }
    internal bool Apply(GoldCreatureCommandEvent incoming, GoldWorldReplica world, Dictionary<uint, GoldReplicatedObject> objects)
    {
        if (incoming is GoldCreatureInteractionEvent interaction) return Interactions.Apply(interaction, world, objects);
        var entry = GoldCreatureCommandCodec.Decode(incoming.Message); ReceivedCount++;
        GoldReplicatedObject? Find(uint reference) => objects.GetValueOrDefault(reference);
        bool Active(GoldReplicatedObject? obj) => obj is not null && (obj.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) == 0;
        bool Creature(GoldReplicatedObject? obj) => obj?.Creature is not null && Active(obj);
        GoldReplicatedObject? subject = null, other = null;
        IReadOnlyList<GoldHitNotification> notifications = Array.Empty<GoldHitNotification>();
        switch (entry)
        {
            case GoldCreatureDefinitionEvent definition:
                // The native helper accepts null context, including after a failed creature cast.
                subject = Find(definition.ContextReference);
                if (subject?.Creature is null || definition.ContextReference == 0) subject = null;
                break;
            case GoldCreatureValueCommandEvent value:
                subject = Find(value.ObjectReference); if (!Creature(subject)) return Reject();
                break;
            case GoldCreatureLinkEvent link:
                subject = Find(link.ParentReference); other = Find(link.ChildReference);
                if (!Creature(subject) || !Creature(other)) return Reject();
                GoldCreatureLinkReducer.Apply(link, objects);
                break;
            case GoldPhysicalHitEvent hit:
                subject = Find(hit.ObjectReference); if (!Active(subject) || subject!.Physical is null) return Reject();
                notifications = GoldPhysicalHitNotifications.Create(hit, subject, objects);
                break;
            case GoldMountTransitionEvent mount:
                subject = Find(mount.ObjectReference); other = Find(mount.PartnerReference);
                if (!Creature(subject)) return Reject();
                GoldMountTransitionReducer.Apply(mount, objects);
                break;
            case GoldFxTargetEvent fx:
                subject = Find(fx.FxReference); other = Find(fx.TargetReference);
                if (!Active(subject) || subject!.Fx is null || !Active(other) || other!.Physical is null) return Reject();
                break;
            case GoldLocalHitCheckEvent check:
                var slot = world.LocalPlayerSlot != 0 ? world.LocalPlayerSlot : world.Session.Teams.LocalPlayerSlot;
                subject = Find(slot);
                if (check.PlayerReference != slot || !Creature(subject)) return Reject();
                break;
            case GoldDialogRegistrationEvent dialog:
                if (!Dialogs.Apply(dialog, world, objects)) return Reject();
                subject = Dialogs.Registrations[^1].Object;
                break;
        }
        _requests.Add(new(entry, subject, other) { HitNotifications = notifications }); return true;
        bool Reject() { RejectedCount++; return false; }
    }
    internal void Clear() { _requests.Clear(); Dialogs.Clear(); Interactions.Clear(); ReceivedCount = RejectedCount = 0; }
}
