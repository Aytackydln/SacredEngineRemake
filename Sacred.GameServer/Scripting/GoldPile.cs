using System.Buffers.Binary;
using Sacred.Network.Gameplay;

namespace Sacred.GameServer.Scripting;

/// <summary>
/// Money on the ground: an ordinary item object whose 130-byte value block carries the amount in its price word.
/// <list type="bullet">
/// <item>Money resources are 0x140C..0x140F (Gold client table 0x949920, tested by 0x43AF50; GameServer.exe table 0x5B0274,
/// tested by 0x409D30). Items.pak models GoldCoins_01/02/05/10.grn, category 15 (not a weapon: no 0xBAADF00D extension).</item>
/// <item>Pile size for an amount: Gold client 0x4AEC1E (dropping gold) picks 0x140C below 50, 0x140D below 350, 0x140E below
/// 1000, else 0x140F, then creates the object and stores the amount with 0x5C7D10 (<c>cItemBase+0x120 = amount</c>).</item>
/// <item>cItemBase+0x118 is the value block (0x5BEB30 returns it; serializer 0x5C7A50 writes 0x82 bytes from +0x118 into the
/// item state), so the amount is the value block's <see cref="Sacred.Core.Pak.Weapon.SacredEquipmentInstanceLayout.Price"/> (+8).</item>
/// <item>Pricing keeps it: client 0x5D3AB0 and GameServer.exe 0x426D80 return the stored price of a money resource when it is
/// nonzero (a random default only for 0: 8..54, 98..534, 986..5456, 9430..53754), so the amount survives import re-pricing
/// (0x602CB5).</item>
/// <item>Pick-up: client 0x558304 (money branch) reads the amount through cItemBase vtable +0x24 property 3 (0x5C70F6: value
/// block +8), removes the object and calls hero values (+0x3A8) AddGold 0x57FD80, which adds it to values +0x46 (the hero's
/// gold, <see cref="GoldCreatureValuesState.Gold"/>), clamped to 0x7FFFFFFE.</item>
/// <item>The rest of the block is the Weapon.pak "Gold" row's instance values (rows 2904..2907, +128): resource, flags
/// 0x00C01D3F, price 600, everything else zero.</item>
/// </list>
/// </summary>
public static class GoldPile
{
    public const uint SmallResource = 0x140C, MediumResource = 0x140D, LargeResource = 0x140E, HugeResource = 0x140F;
    /// <summary>Weapon.pak instance flags of the four money rows (all classes, type byte 0x1D).</summary>
    public const uint ValueFlags = 0x00C01D3F;
    /// <summary>AddGold 0x57FD80 never lets the hero's gold exceed this; 0 would be re-priced to a random default.</summary>
    public const uint MaximumAmount = 0x7FFFFFFE;
    private const int PriceOffset = 8, FlagsOffset = 4;

    public static bool IsMoney(uint resource) => resource is >= SmallResource and <= HugeResource;

    /// <summary>Pile resource the game uses for <paramref name="amount"/> (Gold 0x4AEC1E).</summary>
    public static uint ResourceFor(uint amount) =>
        amount < 50 ? SmallResource : amount < 350 ? MediumResource : amount < 1000 ? LargeResource : HugeResource;

    /// <summary>The 130-byte value block of a pile of <paramref name="amount"/> (1..<see cref="MaximumAmount"/>).</summary>
    public static byte[] Values(uint amount)
    {
        if (amount is 0 or > MaximumAmount) throw new ArgumentOutOfRangeException(nameof(amount));
        var values = new byte[GoldItemStateLayout.ItemDataSize];
        BinaryPrimitives.WriteUInt32LittleEndian(values, ResourceFor(amount));
        BinaryPrimitives.WriteUInt32LittleEndian(values.AsSpan(FlagsOffset), ValueFlags);
        BinaryPrimitives.WriteUInt32LittleEndian(values.AsSpan(PriceOffset), amount);
        return values;
    }
}
