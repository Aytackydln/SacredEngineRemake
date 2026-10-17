namespace Sacred.Network.Gameplay;

/// <summary>Received calendar plus presentation interpolation at the native default rate (Gold 0x4175DE).
/// Does not execute calendar timers, generate weather or advance authoritative script time.</summary>
public sealed class GoldCalendarReplica
{
    public const double DefaultTimeFactor = 24;
    private readonly TimeProvider _time;
    private long _receivedAt;
    public GoldCalendarReplica(TimeProvider? time = null) => _time = time ?? TimeProvider.System;
    public GoldCalendarState? Current { get; private set; }
    public long Revision { get; private set; }
    public float? DayFraction => Current is { HasValidTime: true } calendar
        ? (float)((calendar.DayFraction + Math.Max(0, _time.GetElapsedTime(_receivedAt).TotalSeconds) * DefaultTimeFactor / 86400) % 1) % 1f
        : null;

    public void Apply(GoldWorldEnvironmentEvent environment)
    {
        if (environment.Message.Sender != 0 || environment.Calendar is not { } calendar) return;
        Current = calendar;
        _receivedAt = _time.GetTimestamp();
        Revision++;
    }

    public void Clear() { Current = null; _receivedAt = 0; Revision++; }
}
