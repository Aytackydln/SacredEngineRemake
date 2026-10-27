using Sacred.Core.Runtime;

namespace Sacred.Core.Pak.Weapon;

/// <summary>Gold 0x5C8670..0x5C893B final draw. Live temporary creation, fit
/// and destruction remain explicit effects, executed at their native positions.</summary>
public static class SacredEquipmentSelectionDraw
{
    public static uint? ChooseOffline(IReadOnlyList<SacredEquipmentSelectionCandidate> candidates,
        uint heroClass, SacredRandomSequence random)
        => Choose(candidates, heroClass, random).Resource;

    public static SacredEquipmentSelectionResult Choose(IReadOnlyList<SacredEquipmentSelectionCandidate> candidates,
        uint heroClass, SacredRandomSequence random, bool localHeroPresent = false,
        SacredEquipmentFitEffects? fit = null, SacredEquipmentPreferenceRules? preferences = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(random);
        if (heroClass > 9 && preferences is null) throw new ArgumentOutOfRangeException(nameof(heroClass), "Original resource preference rules are required.");
        if (candidates.Count > 8191) throw new ArgumentException("Exceeds the native resource scan.", nameof(candidates));
        if (candidates.Count == 0) return new(null);
        if (candidates.Count > 1) _ = random.Next(); // Native discarded range-sample draw.
        var preferClass = random.Next() % 2 != 0;
        if (preferClass && heroClass != 0)
        {
            // Original uses a byte count and byte indices, including wraparound.
            // Preserve this observable behavior instead of widening its counters.
            Span<byte> preferred = stackalloc byte[256];
            byte count = 0;
            for (var i = 0; i < candidates.Count; i++)
                if (Preferred(candidates[i].Values.Flags, heroClass, preferences))
                {
                    preferred[count] = unchecked((byte)i);
                    count = unchecked((byte)(count + 1));
                }
            var selected = count == 0 ? candidates.Count : preferred[count == 1 ? 0 : random.Next() % count];
            if (selected < candidates.Count) return new(candidates[selected].ItemType);
        }
        else
        {
            var preferFit = random.Next() % 2 != 0;
            if (preferFit && localHeroPresent)
            {
                uint resource = 0;
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    resource = candidates[candidates.Count == 1 ? 0 : random.Next() % candidates.Count].ItemType;
                    var temporary = fit?.Create(resource);
                    if (!temporary.HasValue) return new(null, SacredEquipmentFitPending.Creation);
                    if (temporary == 0) continue;
                    var accepted = fit!.Fits(temporary.Value);
                    if (!accepted.HasValue) return new(null, SacredEquipmentFitPending.Fit, temporary.Value);
                    if (!fit.Destroy(temporary.Value)) return new(null, SacredEquipmentFitPending.Destruction, temporary.Value);
                    if (accepted.Value) break;
                }
                // Exhaustion returns the third candidate, including failed
                // allocations. It does not reroll or promise equipment fit.
                return new(resource);
            }
        }
        return new(candidates[candidates.Count == 1 ? 0 : random.Next() % candidates.Count].ItemType);
    }

    private static bool Preferred(uint flags, uint heroClass, SacredEquipmentPreferenceRules? preferences)
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
            _ => preferences!.ResourceMasks.GetValueOrDefault(heroClass)
        };
        return (flags & mask) != 0;
    }
}
