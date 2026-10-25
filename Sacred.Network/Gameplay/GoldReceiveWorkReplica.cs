namespace Sacred.Network.Gameplay;

public sealed record GoldCreatureReceiptWork(GoldReceiveWorkEvent Event, GoldReplicatedObject Subject, GoldReplicatedObject? Target = null);
public sealed record GoldControlledObjectWork(GoldControlledObjectEvent Event, GoldReplicatedObject? Previous);

/// <summary>Remaining client receive state and receipt-time work for gameplay, particles, UI and resource consumers.</summary>
public sealed class GoldReceiveWorkReplica
{
    private readonly List<GoldCreatureReceiptWork> _creatureWork = [];
    private readonly List<GoldFxBatchWork> _fxWork = [];
    private readonly List<GoldControlledObjectWork> _controlled = [];
    public GoldReceiveWorkReplica()
    { CreatureWork = _creatureWork.AsReadOnly(); FxWork = _fxWork.AsReadOnly(); ControlledObjects = _controlled.AsReadOnly(); }
    public IReadOnlyList<GoldCreatureReceiptWork> CreatureWork { get; }
    public IReadOnlyList<GoldFxBatchWork> FxWork { get; }
    internal event Action<GoldFxBatchEvent, GoldFxBatchWork>? FxPublished;
    public IReadOnlyList<GoldControlledObjectWork> ControlledObjects { get; }
    public GoldTradeReplica Trade { get; } = new();
    public GoldReferenceReconciliationReplica Reconciliation { get; } = new();
    public GoldStartPositionDataEvent? StartPositions { get; private set; }
    public bool StartPositionsReady { get; private set; }
    public GoldInterpreterStateEvent? Interpreter { get; private set; }
    public GoldCreatureTableEvent? CreatureTable { get; private set; }
    public GoldBannerEvent? Banner { get; private set; }
    public long ResourceVersion { get; private set; }
    public long ReceivedCount { get; private set; }
    /// <summary>5370B0 distance and 44CA30 model sockets need an instantiated actor/model runtime.</summary>
    public Func<GoldReplicatedObject, GoldReplicatedObject, bool>? PairEffectEligibility { get; set; }
    public Func<GoldReplicatedObject, uint?>? PairEffectDistanceFromLocal { get; set; }
    internal bool Apply(GoldReceiveWorkEvent incoming, GoldWorldReplica world, Dictionary<uint, GoldReplicatedObject> objects)
    {
        var entry = (GoldReceiveWorkEvent)GoldGameEventDecoder.Decode(incoming.Message); ReceivedCount++;
        var local = world.LocalPlayerSlot != 0 ? world.LocalPlayerSlot : world.Session.Teams.LocalPlayerSlot;
        GoldReplicatedObject? Creature(uint reference) => objects.GetValueOrDefault(reference) is { Creature: not null } obj ? obj : null;
        switch (entry)
        {
            case GoldExperienceAwardEvent xp:
                var actor = Creature(xp.ObjectReference);
                if (actor is null || (actor.BaseState.Flags & 0x10) != 0) return false;
                // 57E160 is a full progression/linked-recipient operation. Preserve event 213
                // including negative inputs; do not replace it with an unverified XP addition.
                _creatureWork.Add(new(xp, actor)); return true;
            case GoldCreatureEventBlock block:
                var recipient = Creature(block.ObjectReference); if (recipient is null) return false;
                _creatureWork.Add(new(block, recipient)); return true;
            case GoldCreaturePairEffectEvent pair:
                var source = Creature(pair.SourceReference); var target = Creature(pair.TargetReference);
                var hero = Creature(local);
                if (hero is null || source is null || target is null) return false;
                var distance = PairEffectDistanceFromLocal?.Invoke(source);
                if (distance is >= 1500 || PairEffectEligibility?.Invoke(source, target) == false) return false;
                // Missing model/distance inputs leave an explicit work request, without committing
                // +4D4/+4D8 before the native socket and distance guards have been evaluated.
                _creatureWork.Add(new(pair, source, target)); return true;
            case GoldStartPositionDataEvent positions:
                StartPositions = positions;
                if (!positions.DecodedData.IsEmpty) StartPositionsReady = true;
                return true;
            case GoldReferenceReconciliationEvent:
                Reconciliation.Apply(objects, local); return true;
            case GoldTradeEvent trade: return Trade.Apply(trade);
            case GoldControlledObjectEvent controlled:
                var previous = objects.GetValueOrDefault(controlled.Snapshot.ObjectReference);
                _controlled.Add(new(controlled, previous)); world.Apply(controlled.Snapshot); return true;
            case GoldFxPartialEvent partial:
                var old = objects.GetValueOrDefault(partial.ObjectReference);
                if (old?.Fx is null || (old.BaseState.Flags & 0x10) != 0) return false;
                var fx = GoldReceiveWorkCodec.MergePartial(partial, old.Fx);
                objects[partial.ObjectReference] = old with { Fx = fx, FxReloadVersion = old.FxReloadVersion + 1 };
                return true;
            case GoldFxBatchEvent batch:
                foreach (var fxWork in GoldFxBatchCodec.Plan(batch, objects))
                { _fxWork.Add(fxWork); FxPublished?.Invoke(batch, fxWork); }
                return true;
            case GoldInterpreterStateEvent interpreter: Interpreter = interpreter; ResourceVersion++; return true;
            case GoldCreatureTableEvent creatures: CreatureTable = creatures; ResourceVersion++; return true;
            case GoldBannerEvent banner: Banner = banner; ResourceVersion++; return true;
            default: throw new InvalidOperationException("Missing Gold receive work consumer.");
        }
    }
    internal void Clear()
    {
        _creatureWork.Clear(); _fxWork.Clear(); _controlled.Clear(); Trade.Clear(); Reconciliation.Clear();
        StartPositions = null; StartPositionsReady = false; Interpreter = null; CreatureTable = null; Banner = null;
        ReceivedCount = ResourceVersion = 0; PairEffectEligibility = null; PairEffectDistanceFromLocal = null;
    }
}
