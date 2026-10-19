using System.Collections.Generic;
using System.Linq;
using Sacred.Engine.Scene;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

/// <summary>Ordered combat notifications and short-lived damage readouts. Absolute HP messages
/// assign the ordinary character/portrait health after the immediate received damage subtraction.</summary>
internal sealed class NetworkCombatPresentation
{
    private readonly List<(uint Reference, long Instance, ushort Amount, float Age)> _damage = [];
    private long _version;
    public void Update(GoldWorldReplica world, SceneState scene,
        IReadOnlyDictionary<uint, (string Key, NetworkActorPresentation Actor)> actors, float seconds)
    {
        for (var i = _damage.Count - 1; i >= 0; i--)
        {
            var text = _damage[i];
            if (text.Age + seconds >= 1 || !world.Objects.TryGetValue(text.Reference, out var current) ||
                current.InstanceVersion != text.Instance || (current.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0)
                _damage.RemoveAt(i);
            else _damage[i] = (text.Reference, text.Instance, text.Amount, text.Age + seconds);
        }
        foreach (var notification in world.Combat.Recent)
        {
            if (notification.Version <= _version) continue;
            var entry = notification.Event;
            if (!world.Objects.TryGetValue(entry.ObjectReference, out var current) || current.InstanceVersion != notification.InstanceVersion ||
                (current.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0) continue;
            switch (entry)
            {
                case GoldCreatureAttackEvent attack:
                    EngineLog.WriteLine($"Network combat attack: ref {attack.ObjectReference}; {(attack.IsBow ? "bow" : "battle")}; motion {attack.Motion}; rate {attack.Speed:0.###}.");
                    break;
                case GoldCreatureDamageEvent hit:
                    if (notification.PreviousHitPoints != 0)
                        _damage.Add((hit.ObjectReference, notification.InstanceVersion, hit.Damage, 0));
                    EngineLog.WriteLine($"Network combat damage: ref {hit.ObjectReference}; {hit.Damage}; attacker {hit.AttackerReference}; resource {hit.Resource}; impact FX {hit.ImpactEffectEnabled}/{hit.ImpactEffectData}.");
                    break;
                case GoldCreatureHealthEvent health:
                    var delta = notification.PreviousHitPoints is { } previous ? (long)health.HitPoints - previous : (long?)null;
                    EngineLog.WriteLine($"Network combat health: ref {health.ObjectReference}; HP {health.HitPoints}; delta {delta?.ToString() ?? "unknown"}{(delta > 0 ? "; healing/regeneration" : "")}.");
                    break;
            }
        }
        _version = world.Combat.Version;
        scene.CombatText.Damage = _damage.Where(text => actors.ContainsKey(text.Reference))
            .Select(text => new CombatDamageText(actors[text.Reference].Actor.Model, text.Amount, text.Age)).ToArray();
    }
}
