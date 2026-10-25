namespace Sacred.Network.Gameplay;

/// <summary>Presentation-relevant event 208 stores at Gold 53638E..5363D1 and 5364DD.
/// Rewards, scripts, resurrection decisions and authoritative retirement remain owned by the host.</summary>
internal static class GoldCreatureDeathReducer
{
    public static GoldReplicatedObject Apply(GoldReplicatedObject actor, GoldCreatureDeathCommandEvent death) => actor with
    {
        Death = death, DeathVersion = actor.DeathVersion + 1,
        CurrentHitPoints = 0, Values = actor.Values?.WithCurrentHitPoints(0),
        Movement = null, MovementVersion = actor.MovementVersion + 1,
        Attack = null, SpecialMove = null, Cast = null, Sleep = null,
        AttackVersion = actor.AttackVersion + 1,
        Physical = actor.Physical?.WithCorpseFlag()
    };
}
