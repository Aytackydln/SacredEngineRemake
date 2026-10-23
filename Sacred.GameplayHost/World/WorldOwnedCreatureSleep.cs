using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.World;

/// <summary>Owned ordinary-actor sleep/wake slice. Uses original local scheduler inputs;
/// never invents a period or phase for a received snapshot. Runs independently of scene/model loading.</summary>
public sealed class WorldOwnedCreatureSleep(GoldWorldReplica world, IGameplayNetworkTransport transport, Action<string> log,
    Action<GoldReplicatedObject, GoldCreatureUpdateState>? publishState = null)
{
    private sealed class Entry(long instance, GameplayCreatureThinkClock clock)
    {
        public long Instance { get; } = instance;
        public GameplayCreatureThinkClock Clock { get; } = clock;
        public GoldCreatureSleepEvent? Sleep { get; set; }
    }

    private readonly Dictionary<uint, Entry> _entries = [];
    private readonly HashSet<(uint Reference, long Instance)> _reported = [];
    private readonly List<(uint Reference, long Session, Task Task)> _pending = [];
    private long _session = -1;
    private bool _failed;

    public void Advance(float deltaSeconds)
    {
        if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        ObservePublications();
        if (_session != world.SessionVersion)
        {
            _entries.Clear(); _reported.Clear(); _failed = false; _session = world.SessionVersion;
        }
        if (!transport.IsParticipantWorldApproved || transport.PlayerSlot == 0 || world.LocalPlayerSlot != transport.PlayerSlot)
        {
            _entries.Clear(); _reported.Clear();
            return;
        }
        if (_failed || world.Session.IsPaused == true) return;
        foreach (var reference in _entries.Keys.Where(reference => !world.Objects.ContainsKey(reference)).ToArray())
            _entries.Remove(reference);
        // Waking applies a local replica update; enumerate an isolated set of object records.
        foreach (var actor in world.Objects.Values.ToArray()) AdvanceActor(actor, deltaSeconds);
    }

    private void AdvanceActor(GoldReplicatedObject actor, float deltaSeconds)
    {
        if (_failed) return;
        if (actor.Creature is not { } creature || (actor.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0) return;
        var reference = actor.BaseState.ObjectReference;
        if (world.OwnedCreatureThinking.Find(actor) is not { } source || source.Interval < 0)
        {
            if (actor.BaseState.Owner == transport.PlayerSlot && creature.IsSleeping && _reported.Add((reference, actor.InstanceVersion)))
                log($"Network sleep: object {reference} awaits a wake correction; original scheduler inputs unavailable.");
            return;
        }
        if (!_entries.TryGetValue(reference, out var entry) || entry.Instance != actor.InstanceVersion)
            _entries[reference] = entry = new(actor.InstanceVersion, new(source.Interval, source.Elapsed));
        if (!ReferenceEquals(entry.Sleep, actor.Sleep))
        {
            entry.Sleep = actor.Sleep;
            if (actor.Sleep is { } sleep) entry.Clock.Sleep(sleep.ClockOffset);
        }
        // Receipt resets the clock for every owner. Only the current owner advances it.
        if (actor.BaseState.Owner != transport.PlayerSlot) return;
        if (creature.IsSleeping && actor.Sleep is null && _reported.Add((reference, actor.InstanceVersion)))
            log($"Network sleep: object {reference} awaits a wake correction; snapshot omits the scheduler phase.");
        var tick = entry.Clock.Advance(deltaSeconds, (actor.BaseState.Flags & GoldObjectFlightLayout.FlyingFlag) != 0);
        if (!tick || !creature.IsSleeping || actor.Sleep is null ||
            (creature.Word200 & GoldCreatureStateLayout.SleepWakeSuppressedFlag) != 0 ||
            source.KiMode == 9 || source.EiMode == 6 || (actor.CurrentHitPoints ?? actor.Values?.CurrentHitPoints) is not > 0) return;
        var state = GoldCreatureUpdateState.FromCreature(creature).WithFlags(creature.Word200 & ~GoldCreatureStateLayout.SleepingFlag);
        var message = GoldCreatureUpdateCodec.CreateState(transport.PlayerSlot, reference, actor.BaseState.ObjectType, state);
        // Gold 0x539538 clears locally before 0x54D760 publishes the latest partial state.
        world.Apply(GoldGameEventDecoder.Decode(message));
        log($"Network sleep: owned object {reference} woke; publishing current creature state 0x11A.");
        if (publishState is { } publish) { publish(actor, state); return; }
        try { _pending.Add((reference, _session, transport.SendGameplayAsync(message))); }
        catch (Exception error) { PublicationFailed(reference, _session, error); }
    }

    private void ObservePublications()
    {
        for (var index = _pending.Count - 1; index >= 0; index--)
        {
            var (reference, session, task) = _pending[index];
            if (!task.IsCompleted) continue;
            _pending.RemoveAt(index);
            try { task.GetAwaiter().GetResult(); }
            catch (Exception error) { PublicationFailed(reference, session, error); }
        }
    }

    private void PublicationFailed(uint reference, long session, Exception error)
    {
        if (session != world.SessionVersion)
        {
            log($"Network sleep: retired session wake publication for object {reference} failed: {error.Message}");
            return;
        }
        _failed = true;
        log($"Network sleep: wake publication for object {reference} failed; reconnect required: {error.Message}");
    }
}
