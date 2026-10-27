using Sacred.Core.GameBin.Sets;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Runtime;

namespace Sacred.Core.Loot;

public readonly record struct SacredCreatureLootSetRules(uint ExcludedMinimum, uint ExcludedMaximum);
public readonly record struct SacredCreatureLootSetTemplate(uint ValueResource, uint ParentResource, uint Flags,
    byte MinimumLevel, byte SpawnLevel, byte SetType);
/// <summary>Resolved runtime object/predicate/value probes for one equipment
/// slot. The nineteen slots retain their native order, including absent entries.</summary>
public readonly record struct SacredCreatureLootSetEquipment(bool HasObject, bool ExcludedFromScan, uint? ValuesResource);
public sealed record SacredCreatureLootSetContext(uint HeroResource, ushort HeroLevel,
    IReadOnlyList<SacredCreatureLootSetEquipment> Equipment);
public readonly record struct SacredCreatureLootSetResult(uint Resource, uint Selector, int? SetIndex = null, bool? HeroPreferred = null);

/// <summary>Gold 0x55A8E7..0x55B2D3: selector-one set/member selection before
/// concrete object creation. Runtime equipment probes and current shared records
/// are supplied; original class/parent/set definitions own all resource rules.</summary>
public sealed class SacredCreatureLootSetSelection(IReadOnlyList<SacredSetEntry> sets,
    SacredCreatureLootSetRules rules, SacredEquipmentClassRules parents, SacredItemCategoryRules categories,
    float levelScale, Func<uint, SacredCreatureLootSetTemplate?> shared, Func<uint, int?> modifierType)
{
    public SacredCreatureLootSetResult? Select(uint resource, uint selector, bool forced,
        SacredCreatureLootSetContext? hero, SacredRandomSequence random)
    {
        if (hero is null) return null;
        if (selector != 1 || forced) return new(resource, selector);
        var preferred = random.Next() % 100 < 60;
        var mask = hero.HeroResource is >= 1 and <= 9 ? categories.ClassMasks[(int)hero.HeroResource - 1] : categories.AnyClassMask;
        var eligibleSets = new List<int>();
        for (var index = 1; index < sets.Count; index++)
        {
            // The native loop index is wide; its row lookup truncates to byte.
            var set = sets[unchecked((byte)index)];
            if (set.DeclaredItemCount == 0) continue;
            if (preferred)
            {
                var first = Members(set).First();
                var template = shared(first);
                if (!template.HasValue || (template.Value.Flags & mask) == 0 ||
                    unchecked((int)first) >= unchecked((int)rules.ExcludedMinimum) && unchecked((int)first) <= unchecked((int)rules.ExcludedMaximum)) continue;
                if (!ValidModifier(Canonical(template.Value.ValueResource)) || template.Value.MinimumLevel > hero.HeroLevel) continue;
            }
            else if (!Members(set).Any(member => ValidModifier(Canonical(Required(member).ValueResource)))) continue;
            eligibleSets.Add(index);
        }
        if (eligibleSets.Count == 0) return new(resource, 2, HeroPreferred: preferred);
        var selected = eligibleSets[SacredRandomRange.Inclusive(0, eligibleSets.Count - 1, random)];
        var chosen = sets[unchecked((byte)selected)];
        if (hero.Equipment.Count != 19) throw new ArgumentException("Expected the nineteen native equipment slots.", nameof(hero));
        var equipped = new HashSet<uint>();
        foreach (var item in hero.Equipment)
            if (item.HasObject && !item.ExcludedFromScan && item.ValuesResource is { } current && shared(current) is { SetType: not 0 })
                equipped.Add(current);
        var eligibleMembers = new List<uint>();
        foreach (var member in Members(chosen))
            if (ValidModifier(Canonical(member)) && (!equipped.Contains(member) || random.Next() % 100 < 50)) eligibleMembers.Add(member);
        if (eligibleMembers.Count != 0)
        {
            var member = eligibleMembers[SacredRandomRange.Inclusive(0, eligibleMembers.Count - 1, random)];
            var template = shared(member);
            if (template.HasValue && template.Value.SpawnLevel * (double)levelScale < hero.HeroLevel)
                return new(member, 1, selected, preferred);
        }
        // Failure downgrades the selector but keeps the initial resource.
        return new(resource, 2, selected, preferred);
    }

    private static IEnumerable<uint> Members(SacredSetEntry set)
    {
        if (set.DeclaredItemCount > set.ItemIds.Count) throw new InvalidDataException("Set member count exceeds the resolved original table.");
        return set.ItemIds.Take(set.DeclaredItemCount);
    }

    private bool ValidModifier(uint resource) => modifierType(resource) is { } type && type != -1;
    private SacredCreatureLootSetTemplate Required(uint resource) => shared(resource) ??
        throw new InvalidDataException("Native set selection requires this shared equipment record.");

    private uint Canonical(uint resource)
    {
        for (var link = 0; link < parents.MaximumParentLinks && parents.ParentResources.Any(range => range.Contains(resource)); link++)
        {
            var template = shared(resource);
            if (!template.HasValue) return 0;
            resource = template.Value.ParentResource;
        }
        return resource;
    }
}
