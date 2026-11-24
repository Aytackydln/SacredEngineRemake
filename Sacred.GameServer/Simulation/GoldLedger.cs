namespace Sacred.GameServer.Simulation;

/// <summary>
/// The gold ledger (docs/authoritative-world-plan.md, M2): every increase of a hero's gold must be covered by a credit the server
/// saw (a gold pile the player picked up, gold a completed trade gave it). Spending is always accepted. The ledger keeps two
/// numbers per player: what the client reports and what is explained (<see cref="Allowed"/>); the hero is saved with the smaller one
/// when <c>AUTH_STRICT_GOLD=on</c>. Pure, no allocation.
/// <para>Not covered yet (so the mode defaults to off): merchant sales, quest rewards and other gold the client's own game logic pays
/// out. In <c>log</c> mode they show up as unexplained increases.</para>
/// </summary>
public sealed class GoldLedger(WitnessOptions options)
{
    private const int Slots = ItemWitness.MaxSlots + 1;
    private readonly long[] _reported = Fill(-1);
    private readonly long[] _allowed = new long[Slots];
    private readonly long[] _credit = new long[Slots];
    private readonly long[] _creditAtMs = new long[Slots];

    /// <summary>Unexplained increases (count and total gold) since the server started.</summary>
    public int Unexplained { get; private set; }
    public long UnexplainedGold { get; private set; }

    private static long[] Fill(long value) { var a = new long[Slots]; Array.Fill(a, value); return a; }

    public bool Known(byte slot) => slot is >= 1 and <= ItemWitness.MaxSlots && _reported[slot] >= 0;
    public long Allowed(byte slot) => slot is >= 1 and <= ItemWitness.MaxSlots ? _allowed[slot] : 0;
    public long Reported(byte slot) => slot is >= 1 and <= ItemWitness.MaxSlots ? _reported[slot] : -1;

    /// <summary>The player picked up a gold pile, or a trade gave it gold.</summary>
    public void Credit(byte slot, long amount, long now)
    {
        if (slot is 0 or > ItemWitness.MaxSlots || amount <= 0) return;
        Expire(slot, now);
        _credit[slot] = Math.Min(_credit[slot] + amount, 0x7FFFFFFE);
        _creditAtMs[slot] = now + 1;
    }

    /// <summary>A hero state reported <paramref name="gold"/>. Returns the unexplained part of this report (0 when explained). The first
    /// report starts the ledger (the hero's gold when it entered).</summary>
    public long Report(byte slot, long gold, long now)
    {
        if (slot is 0 or > ItemWitness.MaxSlots || gold < 0) return 0;
        if (_reported[slot] < 0) { _reported[slot] = _allowed[slot] = gold; return 0; }
        var delta = gold - _reported[slot];
        _reported[slot] = gold;
        if (delta <= 0) { _allowed[slot] = Math.Max(0, Math.Min(_allowed[slot] + delta, gold)); return 0; }
        Expire(slot, now);
        var covered = Math.Min(delta, _credit[slot]);
        _credit[slot] -= covered;
        _allowed[slot] = Math.Min(_allowed[slot] + covered, gold);
        var unexplained = delta - covered;
        if (unexplained > 0)
        {
            Unexplained++;
            UnexplainedGold += unexplained;
            if (options.StrictGold != Auth.StrictItemsMode.On) _allowed[slot] = Math.Min(_allowed[slot] + unexplained, gold);   // accepted
        }
        return unexplained;
    }

    public void Forget(byte slot)
    {
        if (slot is 0 or > ItemWitness.MaxSlots) return;
        _reported[slot] = -1; _allowed[slot] = _credit[slot] = _creditAtMs[slot] = 0;
    }

    private void Expire(byte slot, long now)
    {
        if (_creditAtMs[slot] != 0 && now + 1 - _creditAtMs[slot] > options.GoldWindowMs) { _credit[slot] = 0; _creditAtMs[slot] = 0; }
    }
}
