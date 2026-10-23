namespace Sacred.GameplayHost.World;

/// <summary>Gold 0x537F3A..0x537FE5 owner AI clock. A tick is a strict threshold crossing,
/// not a countdown reaching zero. This class does not execute the intervening AI/script body.</summary>
public sealed class GameplayCreatureThinkClock
{
    public float Interval { get; private set; }
    public float Elapsed { get; private set; }

    public GameplayCreatureThinkClock(float interval, float elapsed)
    {
        // Negative periods can loop indefinitely in the original subtraction loop.
        if (interval < 0) throw new ArgumentOutOfRangeException(nameof(interval));
        Interval = interval;
        Elapsed = elapsed;
    }

    public void Sleep(float remainingDelay) => Elapsed = Interval - remainingDelay;

    public bool Advance(float deltaSeconds, bool flying)
    {
        if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        if (!flying)
        {
            if (Elapsed < -150 || float.IsNaN(Elapsed)) Elapsed = 0;
            if (Interval > 150) Interval = 5;
            Elapsed += deltaSeconds;
        }
        if (!(Elapsed > Interval)) return false;
        do
        {
            // Native tests the x87 difference before its float32 store is rounded.
            var difference = (double)Elapsed - Interval;
            Elapsed = (float)difference;
            if (difference > (double)Interval * 10) Elapsed = 0;
        } while (Elapsed > Interval);
        return true;
    }
}
