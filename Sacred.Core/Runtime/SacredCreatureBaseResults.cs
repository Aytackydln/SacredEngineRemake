using System.Buffers.Binary;
using Sacred.Core.GameBin.Balance;

namespace Sacred.Core.Runtime;

/// <summary>Gold 0x57B09B..0x57B57F: clamp/apply equipment attributes, set the
/// output mask and add original base combat results. Temporary effects and
/// innate bonuses follow; this does not initialize or finalize an actor.</summary>
public static class SacredCreatureBaseResults
{
    public static void Apply(byte[] values, ReadOnlySpan<int> attributeBonuses, bool ranged,
        SacredCreatureCombatProfileLayout profile, SacredCreatureResultBalanceLayout balance)
        => Apply(SacredX87Precision.Extended, values, attributeBonuses, ranged, profile, balance);

    public static void Apply(SacredX87Precision precision, byte[] values, ReadOnlySpan<int> attributeBonuses, bool ranged,
        SacredCreatureCombatProfileLayout profile, SacredCreatureResultBalanceLayout balance)
    {
        var fp = new SacredResultArithmetic(precision);
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
        var attack = fp.Add(fp.Multiply(strength, profile.AttackStrengthPercent), fp.Multiply(dexterity, profile.AttackDexterityPercent));
        Add(82, fp.Multiply(attack, 0.01f));
        var defense = fp.Add(fp.Multiply(dexterity, profile.DefenseDexterityPercent), fp.Multiply(strength, profile.DefenseStrengthPercent));
        Add(86, fp.Multiply(defense, 0.01f));
        Add(94, fp.Divide(physical + endurance, balance.PhysicalProtectionDivisor));
        Add(98, fp.Divide(dexterity, balance.FireProtectionDivisor));
        Add(102, fp.Divide(mental, balance.MagicProtectionDivisor));
        Add(106, fp.Divide(charisma + endurance, balance.PoisonProtectionDivisor));
        var growth = fp.Subtract(156, balance.AttributeGrowthStart);
        growth = fp.Multiply(fp.Multiply(growth, Word(78)), 0.0064102565f);
        growth = fp.Add(fp.Add(growth, balance.AttributeGrowthStart), 9);
        var chosen = ranged ? 2 : 0;
        Add(158, fp.Divide(fp.Add(Grown(Word(66 + chosen * 2)), values[20 + chosen]), balance.PhysicalDamageDivisor));
        // The AA result remains on the x87 stack until its equipment-attribute
        // addition. Preserve that extra precision; the other three are reloaded.
        var fire = fp.Add(fp.Divide(fp.Add(Grown(Word(70) + Word(66)), (values[22] + values[20]) * 2), balance.FireDamageDivisor), Read(162));
        var magic = fp.Divide(fp.Add(Grown(Word(74)), values[24]), balance.MagicDamageDivisor);
        var magicResult = (float)fp.Add(magic, Read(166));
        var poison = (float)fp.Add(fp.Divide(fp.Add(Grown(Word(76) + Word(68)), (values[25] + values[21]) * 2), balance.PoisonDamageDivisor), Read(170));
        for (var i = 0; i < 4; i++) Add(174 + i * 4, magic);
        Add(158, fp.Divide(bonus[chosen], balance.PhysicalDamageDivisor));
        Write(162, fp.Add(fp.Divide(unchecked(bonus[2] + bonus[0]), balance.FireDamageDivisor), fire));
        Write(166, fp.Add(fp.Divide(bonus[4], balance.MagicDamageDivisor), magicResult));
        Write(170, fp.Add(fp.Divide(unchecked(bonus[5] + bonus[1]), balance.PoisonDamageDivisor), poison));
        for (var i = 0; i < 4; i++) Add(142 + i * 4, 1);
        var resistance = fp.Add(fp.Add(Grown(Word(68)), bonus[1]), values[21]);
        Protection(206, balance.PhysicalProtectionMultiplierDivisor);
        Protection(210, balance.FireProtectionMultiplierDivisor);
        Protection(214, balance.MagicProtectionMultiplierDivisor);
        Protection(218, balance.PoisonProtectionMultiplierDivisor);

        ushort Word(int at) => BinaryPrimitives.ReadUInt16LittleEndian(values.AsSpan(at));
        float Read(int at) => BinaryPrimitives.ReadSingleLittleEndian(values.AsSpan(at));
        void Write(int at, double value) => BinaryPrimitives.WriteSingleLittleEndian(values.AsSpan(at), (float)value);
        void Add(int at, double value) => Write(at, fp.Add(value, Read(at)));
        double Grown(int basis) => fp.Multiply(fp.Multiply(basis, growth), 0.1f);
        void Protection(int at, float divisor) => Write(at, fp.Add(fp.Add(fp.Divide(resistance, divisor), Read(at)), 1));
    }
}
