using System.Collections.Frozen;
using Sacred.Core.CombatArts;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;

namespace Sacred.Assets.CombatArts;

/// <summary>Reconstructs learnable rune pools from Items.pak references and Weapon.pak eligibility.</summary>
public sealed class SacredCombatArtCatalog
{
    public SacredCombatArtCatalog(IEnumerable<ItemsPakEntry> items, IEnumerable<SacredEquipment> equipment)
    {
        var weapons = equipment.ToFrozenDictionary(item => item.IdemId);
        var runes = new List<SacredCombatArtDefinition>();
        foreach (var item in items.Where(item => item.ModelDesc.Category == SacredItemCategory.Rune))
        {
            var code = item.ModelDesc.UpgradeItemType;
            var kind = (SacredCombatArtKind)item.ModelDesc.UpgradeFlags;
            if (code == 0 || kind is not (SacredCombatArtKind.Spell or SacredCombatArtKind.SpecialMove) ||
                SacredCombatArtDefinition.GetNameResource(code) is not { } name ||
                !weapons.TryGetValue(item.ItemIndex, out var weapon)) continue;
            runes.Add(new(code, name, kind, weapon.EffectiveCharacterClassMask, Array.AsReadOnly(new[] { weapon.IdemId })));
        }
        Entries = Array.AsReadOnly(runes.GroupBy(art => (art.Code, art.Kind)).Select(group => new SacredCombatArtDefinition(
            group.Key.Code, group.First().NameResourceId, group.Key.Kind,
            group.Aggregate(SacredCharacterClassMask.None, (mask, art) => mask | art.Classes),
            Array.AsReadOnly(group.SelectMany(art => art.RuneItemIds).Distinct().ToArray())))
            .OrderBy(art => art.Kind).ThenBy(art => art.Code).ToArray());
    }

    public IReadOnlyList<SacredCombatArtDefinition> Entries { get; }
    public IReadOnlyList<SacredCombatArtDefinition> ForCreature(SacredCreatureTemplate? template) =>
        Array.AsReadOnly(Entries.Where(art => art.IsAvailableTo(template)).ToArray());
}
