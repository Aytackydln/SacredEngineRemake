using System.Buffers.Binary;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;

namespace Sacred.Core.Pak.Weapon;

/// <summary>Ordinary Gold 0x5D3AB0 price/requirement calculation, before any embedded
/// socket records exist. Returns price, as native does; the outer caller stores it.
/// Books, currency and merged socket-record pricing require their separate paths.</summary>
public static class SacredEquipmentPricing
{
    public static uint CalculateOrdinary(Span<byte> instance, SacredEquipmentPriceContext context,
        SacredEquipmentPricingBalance balance,
        SacredEquipmentGenerationBalance generation, SacredEquipmentBonusBalance curves,
        SacredEquipmentNormalizationBalance factors, SacredEquipmentSocketBalance sockets, SacredX87Precision precision = SacredX87Precision.Extended)
    {
        if (instance.Length != SacredEquipmentInstanceLayout.Size) throw new ArgumentException("Expected 130-byte item values.", nameof(instance));
        if (context.Category == SacredItemCategory.Book) throw new InvalidOperationException("Book pricing requires the native shared/default price path.");
        var fp = new SacredResultArithmetic(precision);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(instance[4..]);
        var jewelry = context.Category is SacredItemCategory.Ring or SacredItemCategory.Amulet;
        var initialized = (flags & 0x80000000) != 0;
        if (!initialized) instance[25] = 0;
        uint minimumDamage = 0, maximumDamage = 0, protectionTotal = 0;
        for (var i = 0; i < 4; i++)
        {
            minimumDamage = unchecked(minimumDamage + (uint)S(instance, 28 + i * 2));
            maximumDamage = unchecked(maximumDamage + (uint)S(instance, 36 + i * 2));
            protectionTotal = unchecked(protectionTotal + (uint)S(instance, 50 + i * 2));
        }
        var averageDamage = (float)(minimumDamage == 0 ? maximumDamage : fp.Multiply(unchecked(minimumDamage + maximumDamage), .5d));
        // Native 0x5D4108..0x5D419A accumulates physical/fire/poison/magic
        // separately for minima and maxima, then adds those two sums.
        ReadOnlySpan<int> damageOrder = [0, 1, 3, 2];
        var minimumPriceDamage = 0d;
        var maximumPriceDamage = 0d;
        foreach (var i in damageOrder)
        {
            minimumPriceDamage = fp.Add(minimumPriceDamage, fp.Multiply(S(instance, 28 + i * 2), balance.DamageWeights[i]));
            maximumPriceDamage = fp.Add(maximumPriceDamage, fp.Multiply(S(instance, 36 + i * 2), balance.DamageWeights[i]));
        }
        var damage = fp.Multiply(fp.Add(minimumPriceDamage, maximumPriceDamage), .5);
        float priceTerm = 0;
        uint price = 0;
        if (damage > 0) { priceTerm = (float)balance.Damage.Evaluate(damage, precision); price = Truncate(priceTerm); }
        // Native adds poison + magic + physical + fire in this order.
        var protection = fp.Add(fp.Add(fp.Add(fp.Multiply(S(instance, 56), balance.ProtectionWeights[3]), fp.Multiply(S(instance, 54), balance.ProtectionWeights[2])),
            fp.Multiply(S(instance, 50), balance.ProtectionWeights[0])), fp.Multiply(S(instance, 52), balance.ProtectionWeights[1]));
        if (protection > 0)
        {
            priceTerm = (float)balance.Protection.Evaluate(protection, precision);
            price = Truncate(fp.Add(price, priceTerm));
        }
        // The rating stages add current price before the curve's constant.
        ReadOnlySpan<(int Offset, SacredEquipmentPriceCurve Curve)> ratings = [(44, balance.Attack), (46, balance.Defense)];
        foreach (var (offset, curve) in ratings)
        {
            var rating = S(instance, offset);
            if (rating == 0) continue;
            var term = fp.Multiply(fp.Add(fp.Multiply(rating, curve.Quadratic), curve.Linear), rating);
            price = Truncate(fp.Add(fp.Add(term, unchecked((int)price)), curve.Constant));
        }
        var bonus = SacredEquipmentPriceBonuses.Calculate(instance, jewelry, balance, curves, flags, fp);
        price = unchecked(price + bonus.Flat);
        price = Truncate(Math.Max(price, (double)priceTerm));
        price = Truncate(fp.Multiply(price, Math.Max(1, bonus.Multiplier)));
        price = Truncate(fp.Multiply(unchecked((int)price), fp.Add(1, fp.Multiply((flags >> 16) & 15, jewelry ? .5d : .125d))));
        double socketFactor = 1;
        for (var i = 0; i < 8; i++) if (instance[12 + i] is >= 1 and <= 3) socketFactor = fp.Add(socketFactor, sockets.Costs[instance[12 + i] - 1]);
        price = Truncate(fp.Multiply(price, socketFactor));

        var level = Math.Max(1, fp.Add(protectionTotal, averageDamage));
        if (context.HasModifier) level = fp.Divide(fp.Multiply(level, 100), context.Modifier);
        level = fp.Divide(fp.Multiply(Math.Max(1, fp.Subtract(level, generation.LevelFactorZero)), 100), fp.Subtract(generation.LevelFactorHundred, generation.LevelFactorZero));
        var calculatedLevel = (float)level;
        if (!initialized)
        {
            instance[25] = unchecked((byte)Truncate(calculatedLevel > 255 ? 255 : fp.Add(calculatedLevel, .5d)));
            if (instance[25] == 0) instance[25] = 1;
            if ((flags & 0x30000000) != 0 && context.SetType == 0)
                _ = SacredEquipmentBonusNormalization.Apply(instance, context.Category, curves, factors, sockets, precision);
        }
        if (context.Category == SacredItemCategory.Rune)
        {
            price = Truncate(fp.Multiply(price, .02f));
            instance[20] = instance[25] = 1;
            instance.Slice(21, 4).Clear();
        }
        if (price == 0 && BinaryPrimitives.ReadUInt32LittleEndian(instance[8..]) is { } oldPrice && oldPrice != 0) price = oldPrice;
        var result = Truncate(fp.Multiply(price, balance.FinalPriceFactor));
        flags = BinaryPrimitives.ReadUInt32LittleEndian(instance[4..]);
        if (context.ClearClassRestrictions) flags &= 0xFFFFFFC0;
        if (context.ClearLevelAndSkillRequirements) { instance[20] = 0; instance[26] = 0; }
        instance.Slice(21, 4).Clear();
        var requirement = unchecked((byte)Truncate(Math.Max(1, fp.Subtract(fp.Multiply(instance[25] + 9, generation.LevelScale), 8.5d))));
        instance[20] = requirement;
        if (!initialized && instance[122] != 0) instance[122] = requirement;
        BinaryPrimitives.WriteUInt32LittleEndian(instance[4..], flags | 0x80000000);
        return result;
    }

    private static short S(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadInt16LittleEndian(bytes[offset..]);
    internal static uint Truncate(double value) => !double.IsFinite(value) || value >= 9223372036854775808d || value < -9223372036854775808d
        ? 0 : unchecked((uint)(long)value);
}
