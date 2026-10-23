namespace Sacred.Network.Gameplay;

/// <summary>Immediate control-field copies and receipt-time snapshots for native owner callbacks.</summary>
public sealed class GoldCreatureControlReplica
{
    private readonly Queue<GoldCreatureControlReply> _replies = [];
    public int PendingReplies => _replies.Count;
    public long DialogUpdates { get; private set; }
    public long FightModeUpdates { get; private set; }

    internal bool Apply(GoldGameEvent entry, Dictionary<uint, GoldReplicatedObject> objects)
    {
        var decoded = GoldCreatureControlCodec.Decode(entry.Message);
        var reference = decoded is GoldCreatureDialogEvent dialog ? dialog.ObjectReference : ((GoldCreatureFightModeEvent)decoded).ObjectReference;
        if (!objects.TryGetValue(reference, out var actor) || actor.Creature is not { } creature ||
            (actor.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0) return false;
        if (decoded is GoldCreatureDialogEvent update)
        {
            creature = creature.WithControlWord(GoldCreatureStateLayout.Word245Offset, update.DialogIndex);
            // 0x5498F0 requests 0x11A before the receiver overwrites the base flags.
            // Retain each receipt's partial state, including repeated updates in one batch.
            QueueOwnedState(actor, GoldCreatureUpdateState.FromCreature(creature), "dialog setter");
            objects[reference] = actor with { Creature = creature, BaseState = actor.BaseState with { Flags = update.ObjectFlags } };
            DialogUpdates++;
        }
        else
        {
            var fight = (GoldCreatureFightModeEvent)decoded;
            var flags = fight.Enabled ? creature.Word200 | GoldCreatureStateLayout.FightModeFlag : creature.Word200 & ~GoldCreatureStateLayout.FightModeFlag;
            objects[reference] = actor with { Creature = creature.WithControlWord(GoldCreatureStateLayout.Word200Offset, flags) };
            FightModeUpdates++;
        }
        return true;
    }

    public GoldCreatureControlReply? TakeReply() => _replies.TryDequeue(out var reply) ? reply : null;
    /// <summary>Owner clock publications share this FIFO with earlier receipt-time setter replies.</summary>
    public void QueueOwnedState(GoldReplicatedObject actor, GoldCreatureUpdateState state, string reason) =>
        _replies.Enqueue(new(actor.BaseState.ObjectReference, actor.BaseState.ObjectType, actor.InstanceVersion,
            actor.BaseState.Owner, state, reason));
    public void DiscardReplies() => _replies.Clear();
    internal void Clear() { _replies.Clear(); DialogUpdates = FightModeUpdates = 0; }
}
