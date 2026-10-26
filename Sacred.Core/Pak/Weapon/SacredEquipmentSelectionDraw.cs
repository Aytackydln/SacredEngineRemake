using Sacred.Core.Runtime;

namespace Sacred.Core.Pak.Weapon;

/// <summary>Gold 0x5C8670..0x5C893B final draw with no local-world hero.
/// Native class selectors 0..9 are supported. The live hero equipment-fit retry
/// and creature-resource selectors require their separate runtime context.</summary>
public static class SacredEquipmentSelectionDraw
{
    public static uint? ChooseOffline(IReadOnlyList<SacredEquipmentSelectionCandidate> candidates,
        uint heroClass, SacredRandomSequence random)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(random);
        if (heroClass > 9) throw new ArgumentOutOfRangeException(nameof(heroClass));
        if (candidates.Count > 8191) throw new ArgumentException("Exceeds the native resource scan.", nameof(candidates));
        if (candidates.Count == 0) return null;
        if (candidates.Count > 1) _ = random.Next(); // Native discarded range-sample draw.
        var preferClass = random.Next() % 2 != 0;
        if (preferClass && heroClass != 0)
        {
            // Original uses a byte count and byte indices, including wraparound.
            // Preserve this observable behavior instead of widening its counters.
            Span<byte> preferred = stackalloc byte[256];
            byte count = 0;
            for (var i = 0; i < candidates.Count; i++)
                if (Preferred(candidates[i].Values.Flags, heroClass))
                {
                    preferred[count] = unchecked((byte)i);
                    count = unchecked((byte)(count + 1));
                }
            var selected = count == 0 ? candidates.Count : preferred[count == 1 ? 0 : random.Next() % count];
            if (selected < candidates.Count) return candidates[selected].ItemType;
        }
        else
        {
            _ = random.Next(); // World-equipment-fit coin, even without a local hero.
        }
        return candidates[candidates.Count == 1 ? 0 : random.Next() % candidates.Count].ItemType;
    }

    private static bool Preferred(uint flags, uint heroClass)
    {
        // This stage intentionally tests only the low seven bits first; the
        // preceding eligibility stage uses the wider Underworld class mask.
        if ((flags & 0x7F) == 0) return true;
        if ((flags & 0x40) != 0) return false;
        var mask = heroClass switch
        {
            >= 1 and <= 6 => 1u << ((int)heroClass - 1),
            7 => 0x20u,
            8 => 0x400000u,
            9 => 0x800000u,
            _ => 0u
        };
        return (flags & mask) != 0;
    }
}
