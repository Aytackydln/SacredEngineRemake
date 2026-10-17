using System.Collections.ObjectModel;
using Sacred.Assets.Executable;
using Sacred.Core.Pak.Creature;
using Sacred.Network.Gameplay;

namespace Sacred.World.Networking;

/// <summary>Client-local conversation effects on existing replicas. Packet snapshots remain immutable.
/// Ordered receives use the native decision stream; animation clocks come from original clips.</summary>
public sealed class SacredNetworkNpcConversations
{
    private readonly GoldWorldReplica _world;
    private readonly SacredNpcConversationDefinitions _definitions;
    private readonly Func<int> _random;
    private readonly Dictionary<uint, SacredNetworkNpcConversationState> _states = [];
    private readonly long _session;
    private long _version;
    public SacredNetworkNpcConversations(GoldWorldReplica world, SacredNpcConversationDefinitions definitions, Func<int>? random = null)
    {
        (_world, _definitions, _session) = (world, definitions, world.SessionVersion);
        var seed = unchecked((uint)Environment.TickCount);
        _random = random ?? (() => { seed = unchecked(seed * 214013 + 2531011); return (int)((seed >> 16) & 0x7fff); });
        States = new ReadOnlyDictionary<uint, SacredNetworkNpcConversationState>(_states);
    }
    public IReadOnlyDictionary<uint, SacredNetworkNpcConversationState> States { get; }
    public int NeedsAnimationCount => _states.Values.Count(state => state.NeedsAnimation);
    public int NeedsAiResumeCount => _states.Values.Count(state => state.NeedsAiResume);
    public long ReceivedCount { get; private set; }

    public void Update(float seconds)
    {
        if (_world.SessionVersion != _session) { _states.Clear(); return; }
        foreach (var pair in _states.ToArray())
        {
            var state = pair.Value;
            if (!_world.Objects.TryGetValue(pair.Key, out var actor) || !Current(state, actor))
            { _states.Remove(pair.Key); continue; }
            if (state.ActionStage is 0 or 1)
            {
                state.FacingPosition = null;
                if (_world.Objects.TryGetValue(_world.LocalPlayerSlot, out var hero))
                {
                    var idle = SacredNpcClientRules.InitialIdleState(state.BehaviorFlags, Distance(actor, hero));
                    if (idle != 0) { state.SimulationState = idle; state.ActionStage = 0; state.Motion = SacredNpcConversationMotion.None; }
                }
            }
            else if (state.ActionStage == SacredNpcConversationEffect.PlayingAnimationStage && float.IsFinite(seconds) && seconds > 0)
            {
                state.Elapsed += seconds;
                // Gold 0x544F06..0x544F29 resumes at current-end >= 0 (including equality).
                if (state.Elapsed >= state.Duration) state.ActionStage = 1;
            }
        }
        while (_world.NpcIdleAnimations.TryDequeue(out var entry)) Receive(entry!);
    }

    public bool StartAnimation(uint reference, long version, double duration)
    {
        if (!double.IsFinite(duration) || duration < 0) throw new ArgumentOutOfRangeException(nameof(duration));
        if (!_states.TryGetValue(reference, out var state) || state.Version != version || !state.NeedsAnimation) return false;
        state.ActionStage = SacredNpcConversationEffect.PlayingAnimationStage; state.Elapsed = 0; state.Duration = duration;
        return true;
    }

    public SacredNpcOverheadState OverheadState(GoldReplicatedObject actor, GoldReplicatedObject hero)
    {
        var normal = SacredNetworkNpcPresentation.OverheadState(actor, hero);
        return _states.TryGetValue(actor.BaseState.ObjectReference, out var state) && Current(state, actor)
            ? normal with { BehaviorFlags = state.BehaviorFlags, SimulationState = state.SimulationState } : normal;
    }

    public void Clear() => _states.Clear();

    private void Receive(GoldNpcIdleAnimationEvent entry)
    {
        ReceivedCount++;
        var source = entry.ReplicaContext is { } received ? received.Initiator : _world.Objects.GetValueOrDefault(entry.InitiatorReference);
        if (source?.Creature is null || (source.BaseState.Flags & 0x10) != 0) return;
        var target = entry.ReplicaContext is { } context ? context.Target : _world.Objects.GetValueOrDefault(entry.TargetReference);
        if (target?.Creature is null) target = null;
        var effect = SacredNpcConversationRules.Apply(Participant(source), target is null ? null : Participant(target),
            _definitions.Excluded(source.BaseState.ObjectType), _definitions.ReactionType(source.BaseState.ObjectType),
            target is null ? uint.MaxValue : Distance(source, target), _random, _world.ScriptDialogs.GetIcon);
        var state = new SacredNetworkNpcConversationState(source, effect.BehaviorFlags, effect.SimulationState, ++_version)
        { Motion = effect.Motion, ActionStage = effect.Motion == SacredNpcConversationMotion.None ? (ushort)0 : SacredNpcConversationEffect.StartAnimationStage,
          FacingPosition = effect.FaceTarget && target is not null ? SacredNetworkPosition.ToTiles(target.BaseState) : null };
        if (_world.Objects.TryGetValue(entry.InitiatorReference, out var currentSource) && Current(state, currentSource))
            _states[entry.InitiatorReference] = state;
        if (effect.ReceiverState is { } receiverState && target is not null)
        {
            // A self-targeted receive aliases the initiator; do not replace its scheduled action.
            if (!_states.TryGetValue(entry.TargetReference, out var receiver) || !Current(receiver, target))
                _states[entry.TargetReference] = receiver = new(target, target.Creature!.BehaviorFlags, receiverState, ++_version);
            if (_world.Objects.TryGetValue(entry.TargetReference, out var currentTarget) && Current(receiver, currentTarget))
            {
                receiver.SimulationState = receiverState;
                if (effect.FaceReceiver) receiver.FacingPosition = SacredNetworkPosition.ToTiles(source.BaseState);
            }
            else _states.Remove(entry.TargetReference);
        }
    }

    private SacredNpcConversationActor Participant(GoldReplicatedObject actor)
    {
        var creature = actor.Creature!;
        _states.TryGetValue(actor.BaseState.ObjectReference, out var state);
        if (state is not null && !Current(state, actor)) state = null;
        return new(actor.BaseState.ObjectReference, actor.BaseState.Flags, state?.BehaviorFlags ?? creature.BehaviorFlags,
            creature.ServiceFlags, creature.DialogIndex, state?.SimulationState ?? creature.SimulationState,
            state?.ActionStage ?? 0, actor.Values is { CurrentHitPoints: not 0 });
    }

    private static bool Current(SacredNetworkNpcConversationState state, GoldReplicatedObject actor) =>
        (actor.BaseState.Flags & 0x10) == 0 && actor.Creature is not null &&
        !(actor.Values is { CurrentHitPoints: 0 } && state.Source.Values is { CurrentHitPoints: not 0 }) &&
        ReferenceEquals(state.Source.Creature, actor.Creature) && ReferenceEquals(state.Source.Movement, actor.Movement) &&
        state.Source.BaseState.X == actor.BaseState.X && state.Source.BaseState.Y == actor.BaseState.Y &&
        state.Source.BaseState.Sector == actor.BaseState.Sector && state.Source.BaseState.Surface == actor.BaseState.Surface;

    private static uint Distance(GoldReplicatedObject left, GoldReplicatedObject right)
        => SacredNpcDistance.Between(left.BaseState.X, left.BaseState.Y, right.BaseState.X, right.BaseState.Y);
}
