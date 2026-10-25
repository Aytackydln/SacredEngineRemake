using System.Collections.Generic;
using Sacred.Engine.Scene;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

/// <summary>Independent ordered combat diagnostics. Readout playback uses the shared native text pool.</summary>
internal sealed class NetworkCombatPresentation
{
    private GoldConsumerCursor? _cursor;
    public void Update(GoldWorldReplica world, SceneState scene,
        IReadOnlyDictionary<uint, (string Key, NetworkActorPresentation Actor)> actors, float seconds)
    {
        _cursor ??= world.Consumers.CreateCursor("combat-log", work => work.Value is GoldCombatNotification);
        while (world.Consumers.TryPeek(_cursor, out var work))
        {
            var notification = (GoldCombatNotification)work.Value;
            world.Consumers.Report(_cursor, work, GoldConsumerOutcome.Consumed, "combat diagnostics");
            var entry = notification.Event;
            if (!world.Objects.TryGetValue(entry.ObjectReference, out var current) || current.InstanceVersion != notification.InstanceVersion ||
                (current.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0) continue;
            switch (entry)
            {
                case GoldCreatureSleepEvent sleep:
                    EngineLog.WriteLine($"Network creature sleep: ref {sleep.ObjectReference}; native clock offset {sleep.ClockOffset}; motion {GoldCreatureSleepLayout.Motion}; waiting for native wake state.");
                    break;
                case GoldCreatureCastEvent cast:
                    EngineLog.WriteLine($"Network combat spell: ref {cast.ObjectReference}; code {cast.Code}; target {cast.TargetReference} at {cast.TargetX}/{cast.TargetY}; reservations {cast.ReferenceData.Length} bytes.");
                    break;
                case GoldCreatureSpecialMoveEvent special:
                    EngineLog.WriteLine($"Network combat special move: ref {special.ObjectReference}; code {special.Code}; target {special.TargetReference} at {special.TargetX}/{special.TargetY}.");
                    break;
                case GoldCreatureAttackEvent attack:
                    EngineLog.WriteLine($"Network combat attack: ref {attack.ObjectReference}; {(attack.IsBow ? "bow" : "battle")}; motion {attack.Motion}; rate {attack.Speed:0.###}.");
                    break;
                case GoldCreatureDamageEvent hit:
                    EngineLog.WriteLine($"Network combat damage: ref {hit.ObjectReference}; {hit.Damage}; attacker {hit.AttackerReference}; resource {hit.Resource}; impact FX {hit.ImpactEffectEnabled}/{hit.ImpactEffectData}.");
                    break;
                case GoldCreatureHealthEvent health:
                    var delta = notification.PreviousHitPoints is { } previous ? (long)health.HitPoints - previous : (long?)null;
                    EngineLog.WriteLine($"Network combat health: ref {health.ObjectReference}; HP {health.HitPoints}; delta {delta?.ToString() ?? "unknown"}{(delta > 0 ? "; healing/regeneration" : "")}.");
                    break;
            }
        }
        scene.CombatText.Damage = [];
    }
}
