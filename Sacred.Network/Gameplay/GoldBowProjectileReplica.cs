using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace Sacred.Network.Gameplay;

/// <summary>Reserved projectile identities from bow events. Arrows are locally constructed at release,
/// so native 0x167 removal must work even when no ordinary object snapshot exists.</summary>
public sealed class GoldBowProjectileReplica
{
    private readonly Dictionary<uint, GoldBowProjectile> _entries = [];
    private long _generation;
    public GoldBowProjectileReplica() => Entries = new ReadOnlyDictionary<uint, GoldBowProjectile>(_entries);
    public IReadOnlyDictionary<uint, GoldBowProjectile> Entries { get; }
    internal void Add(GoldReplicatedObject actor)
    {
        if (actor.Attack is not { IsBow: true } attack) return;
        var action = new GoldBowActionState(attack.ActionState);
        var count = Math.Min(GoldCombatLayout.MaximumAppliedBowReferenceBytes / sizeof(uint), attack.BowReferenceData.Length / sizeof(uint));
        for (var index = 0; index < count; index++)
        {
            var reference = BinaryPrimitives.ReadUInt32LittleEndian(attack.BowReferenceData.AsSpan(index * 4));
            if (reference == 0) continue;
            _entries[reference] = new(reference, ++_generation, attack, actor.InstanceVersion, actor.AttackVersion, index, count, action)
            {
                CreatureFlags = actor.Creature?.Word200 ?? 0,
                EquipmentReferences = Array.AsReadOnly(actor.Creature!.EquipmentReferences.ToArray())
            };
        }
    }
    internal bool Remove(uint reference) => _entries.Remove(reference);
    /// <summary>Local projectile retirement, guarded against a reused reference.</summary>
    public bool Retire(uint reference, long generation) => _entries.TryGetValue(reference, out var entry) &&
        entry.Generation == generation && _entries.Remove(reference);
    internal bool Notify(uint reference, ushort notification)
    {
        if (!_entries.TryGetValue(reference, out var entry)) return false;
        _entries[reference] = entry with { FxNotification = notification };
        return true;
    }
    internal void Clear() => _entries.Clear();
}
