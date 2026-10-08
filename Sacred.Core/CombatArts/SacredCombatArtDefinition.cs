using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Pak.Weapon.Bonuses;

namespace Sacred.Core.CombatArts;

/// <summary>A learnable art identified by authored rune records, independent of runtime rank.</summary>
public sealed record SacredCombatArtDefinition(ushort Code, uint NameResourceId,
    SacredCombatArtKind Kind, SacredCharacterClassMask Classes, IReadOnlyList<uint> RuneItemIds)
{
    public static uint? GetNameResource(ushort code) => SacredCombatArtResources.GetNameResource(code);

    public bool IsAvailableTo(SacredCreatureTemplate? creature)
    {
        var mask = creature?.PlayableClassMask ?? SacredCharacterClassMask.None;
        return mask != SacredCharacterClassMask.None && (Classes & mask) != 0;
    }
}
