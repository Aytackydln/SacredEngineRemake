namespace Sacred.Network.Gameplay;

/// <summary>Bounded ordered receive history for presentation and diagnostics. Arrival binds the target lifetime;
/// a later full state or reused reference cannot replay an old effect against its replacement.</summary>
public sealed class GoldCombatReplica
{
    private const int HistoryLimit = 256;
    private readonly Queue<GoldCombatNotification> _recent = [];
    public IReadOnlyCollection<GoldCombatNotification> Recent => _recent;
    public long Version { get; private set; }
    public long Attacks { get; private set; }
    public long DamageEvents { get; private set; }
    public long HealthUpdates { get; private set; }
    public long HealingUpdates { get; private set; }

    internal void Add(GoldCombatEvent entry, long instanceVersion, int? previousHitPoints)
    {
        if (entry is GoldCreatureAttackEvent) Attacks++;
        if (entry is GoldCreatureDamageEvent) DamageEvents++;
        if (entry is GoldCreatureHealthEvent health)
        {
            HealthUpdates++;
            if (previousHitPoints is { } before && health.HitPoints > before) HealingUpdates++;
        }
        _recent.Enqueue(new(++Version, instanceVersion, entry, previousHitPoints));
        if (_recent.Count > HistoryLimit) _recent.Dequeue();
    }

    public void Clear()
    {
        _recent.Clear(); Version = Attacks = DamageEvents = HealthUpdates = HealingUpdates = 0;
    }
}

public sealed record GoldCombatNotification(long Version, long InstanceVersion, GoldCombatEvent Event, int? PreviousHitPoints);
