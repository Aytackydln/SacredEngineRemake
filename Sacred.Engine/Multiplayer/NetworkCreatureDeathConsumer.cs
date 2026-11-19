using System;
using System.Collections.Generic;
using System.Linq;
using Sacred.Core.Pak.Creature;
using Sacred.Network.Gameplay;
using Sacred.Particles;
using Sacred.World.Networking;
using Sacred.World.Particles;

namespace Sacred.Engine.Multiplayer;

/// <summary>Death clocks survive asynchronous model loads and equipped-model rebuilding.</summary>
internal sealed class NetworkCreatureDeathConsumer
{
    private readonly Dictionary<uint, State> _states = [];
    // Bit 28 also occurs on living owned/script-created NPCs. It is a corpse
    // cleanup marker only in the death lifecycle, not a universal draw exclusion.
    public bool IsHidden(GoldReplicatedObject actor) => actor.IsDead &&
        ((actor.BaseState.Flags & GoldObjectStateLayout.HiddenFlag) != 0 ||
         _states.TryGetValue(actor.BaseState.ObjectReference, out var state) &&
         state.Instance == actor.InstanceVersion && state.Hidden);

    public void CollectDisappearance(IEnumerable<GoldReplicatedObject> creatures, uint localPlayer,
        SacredParticleQuality quality, Func<GoldReplicatedObject, WorldObjectFxAttachment?> actor, WorldReceivedFxSystem effects)
    {
        foreach (var creature in creatures)
            if (_states.TryGetValue(creature.BaseState.ObjectReference, out var state) && !state.Hidden &&
                state.Age > SacredNetworkCorpseRules.CleanupDelay(quality) &&
                SacredNetworkCorpseRules.CanDisappear(creature, localPlayer) && actor(creature) is { } source &&
                effects.EmitCorpseDisappearance(creature, source))
            {
                state.Hidden = true;
                EngineLog.WriteLine($"Network corpse hidden: ref {creature.BaseState.ObjectReference}; elapsed {state.Age:0.###}; quality {quality}; reference retained.");
            }
    }
    private readonly HashSet<uint> _dead = [];
    private readonly List<uint> _stale = [];

    public void Update(IEnumerable<GoldReplicatedObject> creatures,
        IReadOnlyDictionary<uint, (string Key, NetworkActorPresentation Actor)> actors, float seconds) =>
        UpdateCore(creatures is IReadOnlyList<GoldReplicatedObject> list ? ReadOnlyListSpan.Of(list) : creatures.ToArray(),
            actors, seconds);

    // Runs every frame: no LINQ, no per-frame set or array, and the creature span is walked without an enumerator.
    private void UpdateCore(ReadOnlySpan<GoldReplicatedObject> creatures,
        IReadOnlyDictionary<uint, (string Key, NetworkActorPresentation Actor)> actors, float seconds)
    {
        var dead = _dead;
        dead.Clear();
        foreach (var creature in creatures)
        {
            if (!creature.IsDead) continue;
            var reference = creature.BaseState.ObjectReference; dead.Add(reference);
            if (!_states.TryGetValue(reference, out var state) || state.Instance != creature.InstanceVersion ||
                state.Version != creature.DeathVersion)
                _states[reference] = state = new(creature.InstanceVersion, creature.DeathVersion);
            if (actors.TryGetValue(reference, out var entry) && !ReferenceEquals(state.Actor, entry.Actor) &&
                (entry.Actor.InstanceVersion is null || entry.Actor.InstanceVersion == creature.InstanceVersion))
            {
                var corpse = creature.Death is null && SacredCreatureDeathMotion.IsDeathSlot(creature.Physical?.Word64 ?? 0) ||
                    (creature.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0;
                entry.Actor.ReceiveDeath(creature, state.Age, corpse); state.Actor = entry.Actor;
            }
            state.Age += seconds;
        }
        var stale = _stale;
        stale.Clear();
        foreach (var reference in _states.Keys)
            if (!dead.Contains(reference)) stale.Add(reference);
        foreach (var reference in stale) _states.Remove(reference);
    }

    private sealed class State(long instance, long version)
    {
        public long Instance { get; } = instance;
        public long Version { get; } = version;
        public float Age;
        public bool Hidden;
        public NetworkActorPresentation? Actor;
    }
}


