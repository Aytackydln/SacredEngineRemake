using System;
using System.Collections.Generic;
using System.Linq;
using Sacred.Core.Pak.Creature;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

internal sealed class NetworkHitReactionConsumer
{
    private GoldConsumerCursor? _cursor;
    private readonly Dictionary<long, (GoldCombatNotification Hit, float Age)> _pending = [];
    public void Update(GoldWorldReplica world, IReadOnlyDictionary<uint, (string Key, NetworkActorPresentation Actor)> actors,
        Func<uint, SacredCreatureClass> creatureClass, float seconds)
    {
        _cursor ??= world.Consumers.CreateCursor("creature-hit-reactions", w => w.Value is GoldCombatNotification { Event: GoldCreatureDamageEvent });
        if (_cursor.Session != world.Consumers.Session) _pending.Clear();
        while (world.Consumers.TryPeek(_cursor, out var work))
        {
            var hit = (GoldCombatNotification)work.Value;
            if (GoldCreatureHitReactionRules.ShouldPlay(hit)) _pending[work.Order] = (hit, 0);
            world.Consumers.Report(_cursor, work, GoldConsumerOutcome.Consumed, "evaluated damage reaction control");
        }
        foreach (var (order, state) in _pending.ToArray())
        {
            var reference = state.Hit.Event.ObjectReference;
            if (!world.Objects.TryGetValue(reference, out var current) || current.InstanceVersion != state.Hit.InstanceVersion || current.CurrentHitPoints <= 0)
            { _pending.Remove(order); continue; }
            if (actors.TryGetValue(reference, out var entry) && entry.Actor.InstanceVersion == current.InstanceVersion)
            {
                entry.Actor.ReceiveHitReaction(state.Hit.Subject!, creatureClass(current.BaseState.ObjectType), state.Age);
                _pending.Remove(order);
            }
            else _pending[order] = (state.Hit, state.Age + seconds);
        }
    }
}
