using System.Collections.Generic;
using System.Linq;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

/// <summary>Creature 170 commands must not be stranded in the door-only animation consumer.</summary>
internal sealed class NetworkCreatureAnimationConsumer
{
    private GoldConsumerCursor? _cursor;
    private readonly Dictionary<long, (GoldObjectPresentationWork Work, float Age)> _pending = [];
    public void Update(GoldWorldReplica world,
        IReadOnlyDictionary<uint, (string Key, NetworkActorPresentation Actor)> actors, float seconds)
    {
        _cursor ??= world.Consumers.CreateCursor("creature-object-animations", w =>
            w.Value is GoldObjectPresentationWork { Event: GoldObjectAnimationEvent, Object.Creature: not null });
        if (_cursor.Session != world.Consumers.Session) _pending.Clear();
        while (world.Consumers.TryPeek(_cursor, out var work))
        {
            _pending[work.Order] = ((GoldObjectPresentationWork)work.Value, 0);
            world.Consumers.Report(_cursor, work, GoldConsumerOutcome.Consumed, "registered creature model motion");
        }
        foreach (var (order, state) in _pending.ToArray())
        {
            var reference = state.Work.Object.BaseState.ObjectReference;
            if (!world.Objects.TryGetValue(reference, out var actor) || actor.InstanceVersion != state.Work.Object.InstanceVersion)
            { _pending.Remove(order); continue; }
            if (actors.TryGetValue(reference, out var entry) && entry.Actor.InstanceVersion == actor.InstanceVersion)
            {
                var request = (GoldObjectAnimationEvent)state.Work.Event;
                entry.Actor.ReceiveObjectAnimation(request, actor, state.Age,
                    () => world.CompleteObjectAnimation(state.Work, request.Animation));
                _pending.Remove(order);
            }
            else _pending[order] = (state.Work, state.Age + seconds);
        }
    }
}
