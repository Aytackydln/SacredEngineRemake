using System.Buffers.Binary;
using Sacred.Core.GameBin.Balance;

namespace Sacred.Core.Runtime;

/// <summary>Gold 0x57B09B..0x57B576: clamp/apply equipment attributes, set the
/// output mask and add original base combat results. Temporary effects and
/// innate bonuses follow; this does not initialize or finalize an actor.</summary>
public static class SacredCreatureBaseResults
{
    public static void Apply(byte[] values, ReadOnlySpan<int> attributeBonuses, bool ranged,
        SacredCreatureCombatProfileLayout profile, SacredCreatureResultBalanceLayout balance)
    {
        if (values.Length != SacredActorValuesLayout.Size || attributeBonuses.Length != 6)
            throw new ArgumentException("Expected 238 value bytes and six attribute bonuses in runtime order.");
        Span<int> bonus = stackalloc int[6];
        values[26] = 0;
        for (var i = 0; i < 6; i++)
        {
            var at = 8 + 2 * i;
            var current = Word(at);
            bonus[i] = Math.Max(attributeBonuses[i], -current);
            BinaryPrimitives.WriteUInt16LittleEndian(values.AsSpan(at), unchecked((ushort)(current + bonus[i])));
            if (bonus[i] != 0) values[26] |= (byte)(1 << i);
        }
        var strength = Word(8);
        var endurance = Word(10);
        var dexterity = Word(12);
        var physical = Word(14);
        var mental = Word(16);
        var charisma = Word(18);
        Add(82, (strength * (double)profile.AttackStrengthPercent + dexterity * (double)profile.AttackDexterityPercent) * 0.01f);
        Add(86, (dexterity * (double)profile.DefenseDexterityPercent + strength * (double)profile.DefenseStrengthPercent) * 0.01f);
        Add(94, (physical + (double)endurance) / balance.PhysicalProtectionDivisor);
        Add(98, dexterity / (double)balance.FireProtectionDivisor);
        Add(102, mental / (double)balance.MagicProtectionDivisor);
        Add(106, (charisma + (double)endurance) / balance.PoisonProtectionDivisor);
        var growth = (156d - balance.AttributeGrowthStart) * Word(78) * 0.0064102565f + balance.AttributeGrowthStart + 9;
        var chosen = ranged ? 2 : 0;
        Add(158, (Word(66 + chosen * 2) * growth * 0.1f + values[20 + chosen]) / balance.PhysicalDamageDivisor);
        // The AA result remains on the x87 stack until its equipment-attribute
        // addition. Preserve that extra precision; the other three are reloaded.
        var fire = ((values[22] + values[20]) * 2d + (Word(70) + Word(66)) * growth * 0.1f) / balance.FireDamageDivisor + Read(162);
        var magic = (Word(74) * growth * 0.1f + values[24]) / balance.MagicDamageDivisor;
        var magicResult = (float)(magic + Read(166));
        var poison = (float)(((values[25] + values[21]) * 2d + (Word(76) + Word(68)) * growth * 0.1f) / balance.PoisonDamageDivisor + Read(170));
        for (var i = 0; i < 4; i++) Add(174 + i * 4, magic);
        Add(158, bonus[chosen] / (double)balance.PhysicalDamageDivisor);
        Write(162, unchecked(bonus[2] + bonus[0]) / (double)balance.FireDamageDivisor + fire);
        Write(166, bonus[4] / (double)balance.MagicDamageDivisor + magicResult);
        Write(170, unchecked(bonus[5] + bonus[1]) / (double)balance.PoisonDamageDivisor + poison);
        for (var i = 0; i < 4; i++) Add(142 + i * 4, 1);
        var resistance = growth * Word(68) * 0.1f + bonus[1] + values[21];
        Add(206, resistance / balance.PhysicalProtectionMultiplierDivisor + 1);
        Add(210, resistance / balance.FireProtectionMultiplierDivisor + 1);
        Add(214, resistance / balance.MagicProtectionMultiplierDivisor + 1);
        Add(218, resistance / balance.PoisonProtectionMultiplierDivisor + 1);

        ushort Word(int at) => BinaryPrimitives.ReadUInt16LittleEndian(values.AsSpan(at));
        float Read(int at) => BinaryPrimitives.ReadSingleLittleEndian(values.AsSpan(at));
        void Write(int at, double value) => BinaryPrimitives.WriteSingleLittleEndian(values.AsSpan(at), (float)value);
        void Add(int at, double value) => Write(at, value + Read(at));
    }
}
