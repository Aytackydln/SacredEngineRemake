namespace Sacred.Core.Pak.Creature;

/// <summary>Gold 53EF90..53F31D: death motion selection from authored Models.tmp slots.</summary>
public static class SacredCreatureDeathMotion
{
    public const uint Ordinary = 0xAD;
    public const uint Alternate = 0xAE;
    public const uint Special = 0xE4;
    public static bool IsDeathSlot(uint slot) => slot is 0xA8 or Ordinary or Alternate or >= 0xE4 and <= 0xE9;

    public static uint Select(uint reported, Func<uint, bool> available, Func<int> random,
        bool specialOverride = false)
    {
        if (IsDeathSlot(reported)) return reported;
        var special = available(Special);
        var slot = random() % 100 + 1 >= (special ? 20 : 50) ? Alternate : Ordinary;
        if (slot == Alternate && !available(slot)) slot = Ordinary;
        else if (special && random() % 6 != 0)
        {
            slot = Special;
            if (random() % 2 != 0)
            {
                var variant = 0xE5u + (uint)(random() % 5);
                if (available(variant)) slot = variant;
            }
        }
        if (!available(slot)) slot = Ordinary;
        if (specialOverride && random() % 2 != 0 && special) slot = Special;
        return slot;
    }
}
