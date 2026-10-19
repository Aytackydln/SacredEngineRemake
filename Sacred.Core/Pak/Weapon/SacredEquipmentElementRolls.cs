using Sacred.Core.GameBin.WeaponModifiers;
using Sacred.Core.Runtime;

namespace Sacred.Core.Pak.Weapon;

/// <summary>Gold 0x5CFCC0/0x5D0030 elemental allocation, before normalization to
/// the item's total damage/protection budget. Each chance test consumes a draw.</summary>
internal static class SacredEquipmentElementRolls
{
    public static void Roll(Span<short> values, ReadOnlySpan<SacredModifierRange> ranges,
        float budget, uint difficulty, bool damage, SacredRandomSequence random)
    {
        var selected = 0;
        var lastAmount = 0;
        var penalty = 0;
        var factor = damage ? difficulty switch { 1 => 0.34f, 2 => 0.67f, 3 => 1f, 4 => 1.34f, _ => 0f } : 0f;
        for (var element = 1; element < 4; element++)
        {
            // Poison's chance is tested even if fire and magic already filled both slots.
            if (random.Next() % 100 >= ranges[element].Chance || values[element] != 0 || element == 3 && selected == 2)
                continue;
            var sample = Sample(ranges[element], random);
            var multiplier = damage && element > 1 ? (double)factor + 1 : 1;
            lastAmount = Truncate(Math.Max(1, multiplier * sample * budget * (double)0.01f));
            values[element] = unchecked((short)(values[element] + lastAmount));
            if (damage) penalty = Truncate(lastAmount * (double)factor + penalty);
            selected++;
        }
        if (random.Next() % 100 >= ranges[0].Chance || values[0] != 0) return;
        // Native tests the last unwrapped elemental amount, not the total or count.
        var physical = lastAmount == 0 ? Truncate(budget) :
            Truncate(damage ? Math.Max(0, Sample(ranges[0], random) * (double)budget * (double)0.01f - penalty)
                : Sample(ranges[0], random) * (double)budget * (double)0.01f);
        values[0] = unchecked((short)(values[0] + physical));
    }

    internal static int Sample(SacredModifierRange range, SacredRandomSequence random)
    {
        var minimum = Math.Max(0, range.Minimum);
        var maximum = range.Maximum < 0 ? 1 : range.Maximum;
        if (minimum == maximum) return minimum;
        if (maximum < minimum) (minimum, maximum) = (maximum, minimum);
        return unchecked(minimum + random.Next() % unchecked(maximum - minimum + 1));
    }

    // MSVC __ftol returns the low word of its signed 64-bit truncation.
    internal static int Truncate(double value) => unchecked((int)(long)value);
}
