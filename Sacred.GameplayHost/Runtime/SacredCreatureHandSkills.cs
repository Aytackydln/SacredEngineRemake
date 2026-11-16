using Sacred.Core.Pak.Items;

namespace Sacred.GameplayHost.Runtime;

/// <summary>Resolved weapon/shield inputs. Native skills 3/4/5 read the original
/// shared subtype; skill 13 reads the current own-value subtype instead.</summary>
public readonly record struct SacredCreatureHandEquipment(SacredItemCategory Category,
    byte Usage, byte DefaultTypeCode, byte CurrentTypeCode);

/// <summary>Gold 0x54A2A0..0x54A660 and 0x546F80 for registered weapons and
/// shields in slots 12/13. The usage query's cWeapon3D cast excludes shields.</summary>
public static class SacredCreatureHandSkills
{
    public static SacredCreatureSkillContext Resolve(SacredCreatureHandEquipment? left, SacredCreatureHandEquipment? right)
    {
        Validate(left); Validate(right);
        var usage = Usage(left, right);
        return new(Test(left, 3) || Test(right, 3), Test(left, 4) || Test(right, 4),
            Test(left, 5) || Test(right, 5), usage == 8, Test(left, 7) || Test(right, 7),
            left?.Category == SacredItemCategory.Shield || right?.Category == SacredItemCategory.Shield,
            usage, right is not null, right is { Category: SacredItemCategory.Weapon, CurrentTypeCode: 10 });
    }

    private static ushort Usage(SacredCreatureHandEquipment? left, SacredCreatureHandEquipment? right)
    {
        if (left?.Category != SacredItemCategory.Weapon) left = null;
        if (right?.Category != SacredItemCategory.Weapon) right = null;
        if (left is not null && right is not null)
            return left.Value.Usage == 0 && right.Value.Usage == 0 ? (ushort)0 : (ushort)8;
        var usage = right?.Usage ?? left?.Usage ?? 0;
        return usage == 14 ? (ushort)12 : usage;
    }

    private static bool Test(SacredCreatureHandEquipment? hand, int skill)
    {
        if (hand is not { Category: SacredItemCategory.Weapon } item) return false;
        return skill switch
        {
            3 => item.DefaultTypeCode is 21 or 23 or 24,
            4 => item.DefaultTypeCode <= 4 || item.DefaultTypeCode == 10,
            5 => item.DefaultTypeCode is 5 or 6 or 22,
            7 => item.Usage is 7 or 12 or 13 or 14,
            _ => throw new ArgumentOutOfRangeException(nameof(skill))
        };
    }

    private static void Validate(SacredCreatureHandEquipment? hand)
    {
        if (hand is not null && hand.Value.Category is not (SacredItemCategory.Weapon or SacredItemCategory.Shield))
            throw new NotSupportedException("Initial hand queries require the compared weapon/shield RTTI classes.");
    }
}
