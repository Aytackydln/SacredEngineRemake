using Sacred.Assets.Equipment;
using Sacred.Core.Pak.Weapon;

namespace Sacred.GameServer.Auth;

/// <summary>
/// The price an item should have, computed from its 130-byte value record with the game's own pricing code
/// (<c>SacredEquipmentPricing.CalculateOrdinary</c>, Gold 0x5D3AB0) and the installation's Balance.bin, wpmod.bin and item templates (read-only,
/// <c>--game-dir</c>). The stored price is part of the item's own data and so is whatever a client says; the payment check of the witness
/// (<c>purchase</c>, trader <c>stock</c>) uses this one when <c>AUTH_PRICE_CHECK=on</c>.
/// <para>Checked against 1191 positionless item creations of a 53-minute real session and the saved items of real heroes: plain items
/// (rarity 0) match exactly (800 of 802, every hero item), 38 more are off by one (rounding), and the items with magic bonuses deviate
/// by up to about 25 % in a minority of cases (91 of 1191, 29 of 186 for rarity 1): the bonus pricing is not complete in the repo. So the
/// tolerance is wide: a stated price within <see cref="Tolerance"/> of the computed one is not reported, and the payment check asks for
/// a share of the computed price only (<c>AUTH_PURCHASE_PERCENT</c>, default 25 %).</para>
/// Books, currency and other item kinds the ordinary path does not cover give no price (the stated one is used).
/// </summary>
public sealed class ItemPrices
{
    /// <summary>A stated price this close to the computed one (relative, plus one gold) is accepted without a note.</summary>
    public const double Tolerance = 0.15;

    private readonly Func<uint, byte[], uint?> _expected;

    private ItemPrices(Func<uint, byte[], uint?> expected) => _expected = expected;

    /// <summary>Prices from a function (tests).</summary>
    public static ItemPrices From(Func<uint, byte[], uint?> expected) => new(expected);

    /// <summary>Reads the tables below <paramref name="installation"/>; null with <paramref name="error"/> when they cannot be read.</summary>
    public static ItemPrices? Load(string installation, out string? error)
    {
        error = null;
        SacredEquipmentGenerationData data;
        try { data = SacredEquipmentGenerationData.Load(installation); }
        catch (Exception e) when (e is IOException or InvalidDataException or KeyNotFoundException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            error = e.Message;
            return null;
        }
        return new ItemPrices((type, record) =>
        {
            try
            {
                var tuning = data.Tuning;
                return SacredEquipmentPricing.CalculateOrdinary(record, data.PriceContext(type), tuning.Pricing, tuning.Levels, tuning.Bonuses, tuning.Normalization, tuning.Sockets);
            }
            catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or InvalidDataException or ArgumentException or IndexOutOfRangeException)
            {
                return null;
            }
        });
    }

    /// <summary>The price of an item of <paramref name="type"/> with this 130-byte data block (its own record, after the socket entries).</summary>
    public bool TryExpected(uint type, ReadOnlySpan<byte> data, out uint price)
    {
        price = 0;
        if (data.Length != 130) return false;
        if (_expected(type, data.ToArray()) is not { } computed) return false;        // the calculation writes into the record: a copy
        price = computed;
        return true;
    }

    /// <summary>Whether a stated price is as far from the computed one as the pricing code is known to be off for honest items is not
    /// reported; a clearly different one is.</summary>
    public static bool Deviates(uint stated, uint computed) => Math.Abs((long)stated - computed) > 1 + (long)(computed * Tolerance);
}
