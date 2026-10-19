using System.Buffers.Binary;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.GameBin.WeaponModifiers;
using Sacred.Core.Runtime;

namespace Sacred.Core.Pak.Weapon;

/// <summary>Gold 0x5D4CE0: first accepted authored bonus, before rarity generation.
/// The caller supplies native combat-art class resolution for pooled bonuses.</summary>
public static class SacredEquipmentAuthoredBonusGeneration
{
    public static bool Apply(Span<byte> instance, IReadOnlyList<SacredWeaponModifierBonus> bonuses,
        ushort level, uint difficulty, SacredEquipmentBonusBalance balance,
        SacredRandomSequence random, Func<uint, byte> combatArtClass)
    {
        ArgumentNullException.ThrowIfNull(bonuses);
        ArgumentNullException.ThrowIfNull(balance);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(combatArtClass);
        if (instance.Length != SacredEquipmentInstanceLayout.Size)
            throw new ArgumentException("Expected one native 130-byte item value block.", nameof(instance));
        SacredWeaponModifierBonus? selected = null;
        foreach (var candidate in bonuses)
            if (random.Next() % 100 < candidate.Chance) { selected = candidate; break; }
        if (selected is not { } bonus) return false;
        var slot = 0;
        for (; slot < 8; slot++)
            if (BinaryPrimitives.ReadUInt16LittleEndian(instance[(58 + slot * 2)..]) == 0 &&
                BinaryPrimitives.ReadUInt32LittleEndian(instance[(74 + slot * 4)..]) == 0) break;
        slot = Math.Min(slot, 7);
        var type = bonus.Type;
        var group = BinaryPrimitives.ReadUInt32LittleEndian(instance[(74 + slot * 4)..]);
        if (bonus.Pool == 0) group = bonus.Group;
        else
        {
            var pool = SacredModifierBonusPools.Get(bonus.Pool);
            if (pool.Count != 0) group = pool[pool.Count == 1 ? 0 : random.Next() % pool.Count];
            if ((BinaryPrimitives.ReadUInt32LittleEndian(instance[4..]) & 0xC0003F) == 0xC0003F)
                type |= unchecked((ushort)(combatArtClass(group) << 8));
        }
        var factor = difficulty switch { 1 => 1.34f, 2 => 1.67f, 3 => 2f, 4 => 2.34f, _ => 1f };
        var rolled = (float)(Sample(bonus, random) * (double)factor);
        var curve = balance.Get(unchecked((ushort)group));
        if ((bonus.Minimum == 0 || bonus.Maximum == 0) && bonus.CombatArtLevelDivisor == 0 && bonus.SkillLevelDivisor == 0)
        {
            group = 820; // Native empty-value fallback is a bonus group, not an item resource.
            rolled = 1;
        }
        double value;
        if ((group & 0xFFFF0000) != 0)
        {
            var limit = curve.PercentageMaximum * (double)factor;
            // This branch stores a clamped limit back to a float before truncation.
            value = Math.Max(1, rolled > limit ? (float)limit : rolled);
        }
        else if (curve.LevelScaling < 1.1920929e-7f) value = Math.Max(1, Math.Min(rolled, curve.FlatMaximum * (double)factor));
        else value = Math.Max(1, ((uint)level + 9) * (double)factor * rolled * (double)0.01f);
        var amount = SacredEquipmentElementRolls.Truncate(value);
        var originalCode = bonus.Group & 0xFFFF;
        if (originalCode <= 200)
            amount = Rank(level, bonus.CombatArtLevelDivisor, 20);
        else if (originalCode is >= 600 and <= 798)
            amount = Rank(level, bonus.SkillLevelDivisor, 10);
        BinaryPrimitives.WriteUInt16LittleEndian(instance[(58 + slot * 2)..], type);
        BinaryPrimitives.WriteUInt32LittleEndian(instance[(74 + slot * 4)..], group);
        BinaryPrimitives.WriteInt16LittleEndian(instance[(106 + slot * 2)..], unchecked((short)amount));
        return true;
    }

    private static int Rank(ushort level, int divisor, uint defaultDivisor) =>
        unchecked((int)Math.Max(1, ((uint)level + 9) / (divisor == 0 ? defaultDivisor : unchecked((uint)divisor))));

    private static int Sample(SacredWeaponModifierBonus bonus, SacredRandomSequence random)
    {
        // Equal signed endpoints bypass the native negative-endpoint correction.
        if (bonus.Minimum == bonus.Maximum) return bonus.Minimum;
        return SacredEquipmentElementRolls.Sample(new(0, bonus.Minimum, bonus.Maximum), random);
    }
}
