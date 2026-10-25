namespace Sacred.Network.Gameplay;

/// <summary>Replicated gates before the ordinary, surviving-damage reaction (Gold 531DB9/535889).
/// Native KI/EI words +FC/+150 are absent from this wire prefix; +1FC/+1F8 are unrelated fields.</summary>
public static class GoldCreatureHitReactionRules
{
    public static bool ShouldPlay(GoldCombatNotification hit) =>
        hit is { PreviousHitPoints: > 0, Event: GoldCreatureDamageEvent { Damage: > 0 } damage,
            Subject: { Creature: { } creature } subject } && hit.PreviousHitPoints > damage.Damage &&
        (subject.BaseState.Flags & (GoldObjectLifecycleLayout.InactiveFlag | 2)) == 0 &&
        (creature.Word200 & GoldCreatureStateLayout.HitReactionSuppressedFlag) == 0;
}
