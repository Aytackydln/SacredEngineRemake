using System.Buffers.Binary;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.Pak.Items;

namespace Sacred.Core.Pak.Weapon;

/// <summary>Ordinary Gold 0x5D3AB0 price/requirement calculation, before any embedded
/// socket records exist. Returns price, as native does; the outer caller stores it.
/// Books, currency and merged socket-record pricing require their separate paths.</summary>
public static class SacredEquipmentPricing
{
    public static uint CalculateOrdinary(Span<byte> instance, SacredEquipmentPriceContext context, SacredEquipmentPricingBalance balance,
        SacredEquipmentGenerationBalance generation, SacredEquipmentBonusBalance curves,
        SacredEquipmentNormalizationBalance factors, SacredEquipmentSocketBalance sockets)
    {
        if (instance.Length != SacredEquipmentInstanceLayout.Size) throw new ArgumentException("Expected 130-byte item values.", nameof(instance));
        if (context.Category == SacredItemCategory.Book) throw new InvalidOperationException("Book pricing requires the native shared/default price path.");
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
        var averageDamage = (float)(minimumDamage == 0 ? maximumDamage : unchecked(minimumDamage + maximumDamage) * .5d);
        var damage = 0d;
        for (var i = 0; i < 4; i++) damage += S(instance, 28 + i * 2) * (double)balance.DamageWeights[i];
        for (var i = 0; i < 4; i++) damage += S(instance, 36 + i * 2) * (double)balance.DamageWeights[i];
        damage *= .5;
        float priceTerm = 0;
        uint price = 0;
        if (damage > 0) { priceTerm = (float)balance.Damage.Evaluate(damage); price = Truncate(priceTerm); }
        // Native adds poison + magic + physical + fire in this order.
        var protection = S(instance, 56) * (double)balance.ProtectionWeights[3] + S(instance, 54) * (double)balance.ProtectionWeights[2]
            + S(instance, 50) * (double)balance.ProtectionWeights[0] + S(instance, 52) * (double)balance.ProtectionWeights[1];
        if (protection > 0)
        {
            priceTerm = (float)balance.Protection.Evaluate(protection);
            price = Truncate(price + (double)priceTerm);
        }
        if (S(instance, 44) != 0) price = Truncate(unchecked((int)price) + balance.Attack.Evaluate(S(instance, 44)));
        if (S(instance, 46) != 0) price = Truncate(unchecked((int)price) + balance.Defense.Evaluate(S(instance, 46)));
        var bonus = SacredEquipmentPriceBonuses.Calculate(instance, jewelry, balance, curves, flags);
        price = unchecked(price + bonus.Flat);
        price = Truncate(Math.Max(price, (double)priceTerm));
        price = Truncate(price * (double)Math.Max(1, bonus.Multiplier));
        price = Truncate(unchecked((int)price) * (1d + ((flags >> 16) & 15) * (jewelry ? .5d : .125d)));
        double socketFactor = 1;
        for (var i = 0; i < 8; i++) if (instance[12 + i] is >= 1 and <= 3) socketFactor += sockets.Costs[instance[12 + i] - 1];
        price = Truncate(price * socketFactor);

        var level = Math.Max(1, protectionTotal + (double)averageDamage);
        if (context.HasModifier) level = level * 100 / context.Modifier;
        level = Math.Max(1, level - generation.LevelFactorZero) * 100 / (generation.LevelFactorHundred - (double)generation.LevelFactorZero);
        var calculatedLevel = (float)level;
        if (!initialized)
        {
            instance[25] = unchecked((byte)Truncate(calculatedLevel > 255 ? 255 : calculatedLevel + .5d));
            if (instance[25] == 0) instance[25] = 1;
            if ((flags & 0x30000000) != 0 && context.SetType == 0)
                _ = SacredEquipmentBonusNormalization.Apply(instance, context.Category, curves, factors, sockets);
        }
        if (context.Category == SacredItemCategory.Rune)
        {
            price = Truncate(price * (double).02f);
            instance[20] = instance[25] = 1;
            instance.Slice(21, 4).Clear();
        }
        if (price == 0 && BinaryPrimitives.ReadUInt32LittleEndian(instance[8..]) is { } oldPrice && oldPrice != 0) price = oldPrice;
        var result = Truncate(price * (double)balance.FinalPriceFactor);
        flags = BinaryPrimitives.ReadUInt32LittleEndian(instance[4..]);
        if (context.ClearClassRestrictions) flags &= 0xFFFFFFC0;
        if (context.ClearLevelAndSkillRequirements) { instance[20] = 0; instance[26] = 0; }
        instance.Slice(21, 4).Clear();
        var requirement = unchecked((byte)Truncate(Math.Max(1, (instance[25] + 9) * (double)generation.LevelScale - 8.5d)));
        instance[20] = requirement;
        if (!initialized && instance[122] != 0) instance[122] = requirement;
        BinaryPrimitives.WriteUInt32LittleEndian(instance[4..], flags | 0x80000000);
        return result;
    }

    private static short S(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadInt16LittleEndian(bytes[offset..]);
    internal static uint Truncate(double value) => !double.IsFinite(value) || value >= 9223372036854775808d || value < -9223372036854775808d
        ? 0 : unchecked((uint)(long)value);
}
