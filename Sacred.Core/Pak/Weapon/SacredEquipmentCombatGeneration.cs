using System.Buffers.Binary;
using Sacred.Core.GameBin.WeaponModifiers;
using Sacred.Core.Runtime;

namespace Sacred.Core.Pak.Weapon;

/// <summary>Gold 0x5D0280/0x5D0600 damage and protection stages of item generation.
/// Attack and defense use the following 0x5D5390 stage. Other fields are retained.
/// This does not generate requirements, bonuses,
/// sockets, rarity or price, and does not itself constitute a complete generated item.</summary>
public static class SacredEquipmentCombatGeneration
{
    public static void Apply(Span<byte> instance, in SacredWeaponModifierLayout definition,
        float levelFactor, uint difficulty, SacredRandomSequence random, SacredX87Precision precision = SacredX87Precision.Extended)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (instance.Length != SacredEquipmentInstanceLayout.Size)
            throw new ArgumentException("Expected one native 130-byte item value block.", nameof(instance));
        if (!float.IsFinite(levelFactor)) throw new ArgumentOutOfRangeException(nameof(levelFactor));
        var fp = new SacredResultArithmetic(precision);
        Damage(instance, definition, levelFactor, difficulty, random, fp);
        Protection(instance, definition, levelFactor, random, fp);
        Rating(instance, 44, definition.Attack, levelFactor, random, fp);
        Rating(instance, 46, definition.Defense, levelFactor, random, fp);
    }

    private static void Damage(Span<byte> instance, in SacredWeaponModifierLayout definition,
        float levelFactor, uint difficulty, SacredRandomSequence random, SacredResultArithmetic fp)
    {
        instance.Slice(36, 8).Clear();
        Span<SacredModifierRange> ranges = [definition.PhysicalDamage, definition.FireDamage,
            definition.MagicDamage, definition.PoisonDamage];
        if (!HasChance(ranges)) return; // The native zero-chance return retains the minima.
        var budget = Budget(definition.Modifier, levelFactor, fp);
        NormalizeRanges(ranges);
        Span<short> values = stackalloc short[4];
        values.Clear();
        SacredEquipmentElementRolls.Roll(values, ranges, budget, difficulty, true, random, fp);
        NormalizeValues(values, budget, fp);
        var spread = Math.Min(100d, fp.Multiply(definition.Variance, 0.01f));
        instance.Slice(28, 8).Clear();
        for (var element = 0; element < values.Length; element++)
        {
            var value = values[element];
            if (value == 0) continue;
            Write(instance, 28 + element * 2, SacredEquipmentElementRolls.Truncate(Math.Max(1, fp.Subtract(value, fp.Multiply(value, spread)))));
            Write(instance, 36 + element * 2, SacredEquipmentElementRolls.Truncate(fp.Multiply(fp.Add(spread, 1), value)));
        }
    }

    private static void Protection(Span<byte> instance, in SacredWeaponModifierLayout definition,
        float levelFactor, SacredRandomSequence random, SacredResultArithmetic fp)
    {
        instance.Slice(50, 8).Clear();
        Span<SacredModifierRange> ranges = [definition.PhysicalProtection, definition.FireProtection,
            definition.MagicProtection, definition.PoisonProtection];
        if (!HasChance(ranges)) return;
        var budget = Budget(definition.Modifier, levelFactor, fp);
        NormalizeRanges(ranges);
        Span<short> values = stackalloc short[4];
        values.Clear();
        SacredEquipmentElementRolls.Roll(values, ranges, budget, 0, false, random, fp);
        NormalizeValues(values, budget, fp);
        for (var element = 0; element < values.Length; element++) Write(instance, 50 + element * 2, values[element]);
    }

    private static float Budget(int modifier, float levelFactor, SacredResultArithmetic fp) =>
        (float)Math.Max(1, fp.Add(fp.Multiply(fp.Multiply(modifier, levelFactor), 0.01f), 0.5d));

    private static void Rating(Span<byte> instance, int offset, SacredModifierRange range,
        float levelFactor, SacredRandomSequence random, SacredResultArithmetic fp)
    {
        if (random.Next() % 100 < range.Chance)
            Write(instance, offset, SacredEquipmentElementRolls.Truncate(
                fp.Multiply(fp.Multiply(SacredEquipmentElementRolls.Sample(range, random), levelFactor), 0.01f)));
    }

    private static bool HasChance(ReadOnlySpan<SacredModifierRange> ranges) =>
        ranges[0].Chance != 0 || ranges[1].Chance != 0 || ranges[2].Chance != 0 || ranges[3].Chance != 0;

    private static void NormalizeRanges(Span<SacredModifierRange> ranges)
    {
        for (var i = 0; i < ranges.Length; i++)
            if (ranges[i].Maximum <= ranges[i].Minimum)
                ranges[i] = ranges[i] with { Maximum = unchecked(ranges[i].Minimum + 1) };
    }

    private static void NormalizeValues(Span<short> values, float budget, SacredResultArithmetic fp)
    {
        var sum = values[0] + values[1] + values[2] + values[3];
        if (sum == 0)
        {
            sum = SacredEquipmentElementRolls.Truncate(budget);
            values[0] = unchecked((short)sum);
            if (sum == 0) return;
        }
        var normalizedSum = 0;
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = unchecked((short)SacredEquipmentElementRolls.Truncate(fp.Divide(fp.Multiply(values[i], budget), sum)));
            normalizedSum += values[i];
        }
        var remainder = SacredEquipmentElementRolls.Truncate(fp.Add(fp.Subtract(budget, normalizedSum), 0.5d));
        for (var i = 0; i < values.Length; i++)
            if (values[i] != 0) { values[i] = unchecked((short)(values[i] + remainder)); break; }
    }

    private static void Write(Span<byte> bytes, int offset, int value) =>
        BinaryPrimitives.WriteInt16LittleEndian(bytes[offset..], unchecked((short)value));
}
