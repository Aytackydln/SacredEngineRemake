using Sacred.Core.GameBin.Balance;
using Sacred.Core.Runtime;

namespace Sacred.Core.Loot;

/// <summary>Gold 0x55B33C..0x55B3C7: amount on a selected money resource.</summary>
public static class SacredCreatureLootMoney
{
    public static int Calculate(ushort sourceLevel, SacredLootBalance balance, SacredRandomSequence random)
    {
        var amount = unchecked((sourceLevel - 1) * (balance.CreatureMoneyMaximum - balance.CreatureMoneyMinimum)) / 100;
        amount = unchecked(amount + balance.CreatureMoneyMinimum);
        var maximum = amount / 3;
        if (maximum < 0) maximum = 1;
        var sample = maximum == 0 ? 0 : random.Next() % (maximum + 1);
        return Math.Max(1, unchecked(amount + sample - amount / 6));
    }
}
