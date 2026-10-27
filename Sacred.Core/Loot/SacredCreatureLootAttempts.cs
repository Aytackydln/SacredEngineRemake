using Sacred.Core.GameBin.Balance;
using Sacred.Core.Runtime;

namespace Sacred.Core.Loot;

public readonly record struct SacredCreatureLootAttempt(int Pass, int Attempt, int RemainingBudget, uint Bonus);

public enum SacredCreatureLootAttemptOutcome
{
    CompletedPass,
    RetrySelection,
    Unresolved
}

/// <summary>Gold 0x559F17..0x55C59D. Selection/construction owns the shared
/// RNG between attempts; only native selection rejection requests a retry.</summary>
public static class SacredCreatureLootAttempts
{
    /// <returns>False when the caller reports an unresolved dependency. No
    /// subsequent attempt or RNG draw is made in that case.</returns>
    public static bool Run(int initialBudget, bool forcedResource, uint initialBonus,
        uint sourceReference, ushort experienceMaximum, SacredLootBalance balance,
        SacredRandomSequence random, Func<SacredCreatureLootAttempt, SacredCreatureLootAttemptOutcome> select)
    {
        // The native countdown lives in a float; every pass subtracts 1000,
        // truncates to a signed DWORD and stores it back as a float.
        var remaining = (float)initialBudget;
        var budget = initialBudget;
        var bonus = initialBonus;
        for (var pass = 1; remaining > 0; pass++)
        {
            for (var attempt = 1; attempt <= 6; attempt++)
            {
                if (!forcedResource && random.Next() % 1000 >= budget) break;
                bonus = SacredCreatureLootBonus.Adjust(bonus, sourceReference, experienceMaximum, balance);
                var outcome = select(new(pass, attempt, budget, bonus));
                if (outcome == SacredCreatureLootAttemptOutcome.Unresolved) return false;
                if (outcome == SacredCreatureLootAttemptOutcome.CompletedPass) break;
                if (outcome != SacredCreatureLootAttemptOutcome.RetrySelection)
                    throw new ArgumentOutOfRangeException(nameof(select), "Unknown loot selection outcome.");
            }
            budget = (int)(remaining - 1000d);
            remaining = budget;
        }
        return true;
    }
}
