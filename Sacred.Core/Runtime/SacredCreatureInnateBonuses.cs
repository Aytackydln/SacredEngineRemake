using System.Buffers.Binary;
using Sacred.Core.Pak.Creature;

namespace Sacred.Core.Runtime;

/// <summary>Gold CalcResults' six authored Creature.pak bonus rows, 0x57B6E4..
/// 0x57C141. Applies in archive order to the current result accumulators. Weapon
/// predicates are resolved by the actor, not inferred from an item display name.</summary>
public static class SacredCreatureInnateBonuses
{
    public static void Apply(byte[] values, IReadOnlyList<SacredCreatureInnateBonus> bonuses, bool unarmed, bool ranged)
    {
        if (values.Length != SacredActorValuesLayout.Size) throw new ArgumentException("Expected 238-byte actor values.", nameof(values));
        if (bonuses.Count != 6) throw new ArgumentException("Expected six authored innate rows.", nameof(bonuses));
        for (var i = 0; i < bonuses.Count; i++)
            if (bonuses[i].CreatureClass == 0 && bonuses[i].SourceOperation is >= 23 and <= 36)
                throw new NotSupportedException($"Innate row {i}, operation {bonuses[i].SourceOperation}, requires the original legacy power calculation.");
        foreach (var bonus in bonuses)
        {
            if (bonus.Code == 0 || bonus.CreatureClass != 0) continue;
            var mode = bonus.Code & 255;
            var target = bonus.Code >> 8;
            if (mode == 0)
            {
                var amount = (float)(bonus.Percent * (double)0.01f);
                AddMultiplier(target, amount);
                // The native constant-selector pass is additional, including
                // the low destinations already processed above.
                if (target is >= 1 and <= 4 && unarmed) Add(110 + (target - 1) * 4, amount);
                else if (target is >= 5 and <= 8) Add(94 + (target - 5) * 4, amount);
                else if (target is >= 9 and <= 12) Add(142 + (target - 9) * 4, -amount);
                else if (target is >= 13 and <= 16) Add(206 + (target - 13) * 4, -amount);
                else if (target is 17 or 18)
                {
                    var offset = target == 17 ? 82 : 86;
                    Write(offset, Read(offset) - (double)amount * Read(offset));
                }
            }
            else if (mode is >= 1 and <= 7)
            {
                var amount = bonus.Percent * (double)Source(mode) * 0.01f;
                if (target is >= 1 and <= 4 && unarmed)
                {
                    Add(110 + (target - 1) * 4, amount);
                    Write(126 + (target - 1) * 4, amount * 0.5 + Read(126 + (target - 1) * 4) + 1);
                }
                else if (target is >= 5 and <= 8) Add(94 + (target - 5) * 4, amount);
            }
            else if (mode is >= 8 and <= 14 && !ranged)
                AddMultiplier(target, bonus.Percent * (double)Source(mode - 7) * 0.0001f);
            else if (mode == 15 && ranged)
            {
                for (var i = 0; i < 4; i++) Add(142 + i * 4, bonus.Percent * (double)0.01f);
            }
            else if (mode is >= 16 and <= 22 && ranged && target is >= 1 and <= 4)
                Add(142 + (target - 1) * 4, bonus.Percent * (double)Source(mode - 15) * 0.0001f);
        }

        ushort Source(int mode) => BinaryPrimitives.ReadUInt16LittleEndian(values.AsSpan(mode == 7 ? 78 : AttributeOffset(mode)));
        float Read(int offset) => BinaryPrimitives.ReadSingleLittleEndian(values.AsSpan(offset));
        void Write(int offset, double value) => BinaryPrimitives.WriteSingleLittleEndian(values.AsSpan(offset), (float)value);
        void Add(int offset, double value) => Write(offset, value + Read(offset));
        void AddMultiplier(int target, double value)
        {
            if (target is >= 1 and <= 4) Add(142 + (target - 1) * 4, value);
            else if (target is >= 5 and <= 8) Add(206 + (target - 5) * 4, value);
        }
    }

    // BonusAuf's attribute selectors have their own order, distinct from both
    // runtime words and the archive's six seed bytes.
    private static int AttributeOffset(int selector) => selector switch
    {
        1 => 8, 2 => 10, 3 => 14, 4 => 16, 5 => 12, 6 => 18,
        _ => throw new ArgumentOutOfRangeException(nameof(selector))
    };
}
