using Sacred.Core.Pak.Creature;

namespace Sacred.Core.CombatArts;

/// <summary>Native single-player new-hero acquisition, independent of equipment and rune generation.
/// Demo initSpells 0x5067A0 and Gold 0x5600E0 initialize these arts at base level one.</summary>
public static class SacredCombatArtStartingAllocation
{
    /// <summary>Includes automatic transformations without a consumable rune.
    /// Such definitions do not expand the authored rune pool.</summary>
    public static IReadOnlyList<SacredCombatArtDefinition> DefinitionsForCreature(SacredCreatureTemplate? template) =>
        ForCreature(template).Select(code => new SacredCombatArtDefinition(code,
            SacredCombatArtDefinition.GetNameResource(code) ?? throw new InvalidDataException("Unnamed native starting art."),
            code < 1000 ? SacredCombatArtKind.Spell : SacredCombatArtKind.SpecialMove,
            template!.PlayableClassMask, [])).ToArray();

    public static IReadOnlyList<ushort> ForCreature(SacredCreatureTemplate? template) =>
        template?.Class != SacredCreatureClass.Hero ? [] : template.ItemType switch
        {
            1 => [1071],
            2 => [1059],
            3 => [5],
            4 => [1066],
            5 => [1062],
            // Both native Vampiress forms receive Knight -> Vampire and Vampire -> Knight.
            6 or 7 => [1022, 1023],
            8 => [1080],
            9 => [1097],
            _ => []
        };
}
