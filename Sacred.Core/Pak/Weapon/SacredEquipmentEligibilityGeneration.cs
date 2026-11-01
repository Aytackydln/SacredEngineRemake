using System.Buffers.Binary;
using Sacred.Core.GameBin.WeaponModifiers;
using Sacred.Core.Runtime;

namespace Sacred.Core.Pak.Weapon;

/// <summary>Gold 0x5D546E..0x5D5574, after combat rolls and before authored bonuses.
/// Clears the four attribute requirements and applies class/skill eligibility rows.</summary>
public static class SacredEquipmentEligibilityGeneration
{
    public static void Apply(Span<byte> instance, in SacredWeaponModifierLayout definition,
        ushort level, SacredRandomSequence random, SacredX87Precision precision = SacredX87Precision.Extended)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (instance.Length != SacredEquipmentInstanceLayout.Size)
            throw new ArgumentException("Expected one native 130-byte item value block.", nameof(instance));
        var fp = new SacredResultArithmetic(precision);
        instance.Slice(21, 4).Clear();
        ApplyRow(instance, definition.Eligibility0, definition.RequirementType0, level, random, fp);
        ApplyRow(instance, definition.Eligibility1, definition.RequirementType1, level, random, fp);
        ApplyRow(instance, definition.Eligibility2, definition.RequirementType2, level, random, fp);
    }

    private static void ApplyRow(Span<byte> instance, SacredEquipmentEligibilityLayout rule,
        int selector, ushort level, SacredRandomSequence random, SacredResultArithmetic fp)
    {
        if (rule.Chance > 100 || selector == 0 || random.Next() % 100 >= rule.Chance) return;
        var divisor = rule.LevelDivisor == 0 ? 5 : rule.LevelDivisor;
        var rank = SacredEquipmentElementRolls.Truncate(fp.Add(fp.Divide(level, divisor), 0.5d));
        uint classFlag = selector switch
        {
            10 => 2,
            11 => 1,
            12 => 0x10,
            13 => 8,
            14 => 4,
            15 => 0x20,
            16 => 0x400000,
            17 => 0x800000,
            _ => 0
        };
        if (classFlag != 0)
            BinaryPrimitives.WriteUInt32LittleEndian(instance[4..],
                BinaryPrimitives.ReadUInt32LittleEndian(instance[4..]) | classFlag);
        if (selector > 100)
        {
            instance[26] = unchecked((byte)(selector - 100));
            instance[27] = unchecked((byte)rank);
        }
    }
}
