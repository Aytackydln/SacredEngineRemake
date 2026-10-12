using Sacred.Core.Pak.Creature;
using Sacred.Inventory.CombatArts;
using Sacred.Inventory.Stats;

namespace Sacred.Inventory.Actors;

/// <summary>The actor's cCreatureHero component: character data, arts and independent runtime HP.</summary>
public sealed class SacredActorCharacter(SacredCreatureTemplate? template)
{
    public SacredCharacterProgression Progression { get; } = new(template);
    public SacredActorCombatArts CombatArts => Progression.CombatArts;
    public SacredActorHealth Health { get; } = new();
}
