using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.World;

/// <summary>Consumes receipt-time text positions independently of actors/models and diagnostic history.</summary>
public sealed class GameplayReceivedTexts
{
    private readonly GoldWorldReplica _world;
    private readonly GoldConsumerCursor _cursor;
    private long _session = -1;
    public GameplayReceivedTexts(GoldWorldReplica world)
    {
        _world = world;
        _cursor = world.Consumers.CreateCursor("world-text", work => work.Value is GoldCombatNotification
            { Event: GoldCreatureTextEvent or GoldCreatureDamageEvent });
    }
    public GameplayWorldTextPool Pool { get; } = new();
    public void Advance(float seconds)
    {
        if (_session != _world.Consumers.Session) { Pool.Clear(); _session = _world.Consumers.Session; }
        Pool.Advance(seconds);
        while (_world.Consumers.TryPeek(_cursor, out var work))
        {
            var notice = (GoldCombatNotification)work.Value;
            GameplayWorldText? text = null;
            if (notice.Event is GoldCreatureTextEvent { ReceiptPosition: { } position } resource)
                text = new(resource.TextId, position.X, position.Y, GoldCreatureTextLayout.ColorArgb);
            else if (notice.Event is GoldCreatureDamageEvent damage && notice.PreviousHitPoints != 0 && notice.Subject is { } actor)
            {
                // Gold 0x53257A: linked creature, player reference, or ordinary foreign creature.
                var color = actor.Creature!.Word251 is > 0 and <= 16 ? 0xFFFF00FFu
                    : actor.BaseState.ObjectReference <= 16 ? 0xFFFF0000u : 0xFFFFFF00u;
                text = new(0, actor.BaseState.X, actor.BaseState.Y, color, damage.Damage);
            }
            var added = text is not null && Pool.Add(text);
            _world.Consumers.Report(_cursor, work, GoldConsumerOutcome.Consumed,
                text is null ? "native zero-HP exclusion" : added ? $"slot allocated; resource {text.Resource}, digit {text.Digit}" : "native text pool full; dropped");
        }
    }
}
