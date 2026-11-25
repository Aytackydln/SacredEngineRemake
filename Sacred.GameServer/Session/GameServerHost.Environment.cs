using System.Buffers.Binary;

namespace Sacred.GameServer.Session;

/// <summary>
/// World environment 0x00B (<c>FUN_004d4230</c>, 508 bytes): [0] calendar present, [1] weather present, [3] 1, [4..20) the
/// calendar (year i32, month, day, hours, minutes, seconds u16, 2 bytes padding), [20..52) weather, the rest further world
/// state. The original sends it to a newly admitted player (window message 0x40E from the admission), to every connected
/// player about every 30 s (0x4D8C3E) and to an in-game player after each sector entry (case 0x4C7202).
/// The server keeps its own calendar running at the game's default rate (24 game seconds per second). Weather and the
/// trailing state are not simulated yet and stay zero.
/// </summary>
public sealed partial class GameServerHost
{
    private const int EnvironmentBytes = 0x1FC, EnvironmentIntervalMs = 30_000, CalendarTimeFactor = 24;
    private readonly byte[] _environment = new byte[EnvironmentBytes];
    private long _nextEnvironmentMs = EnvironmentIntervalMs;

    // Calendar start as seen from GameServer.exe in a fresh session (year 1370, February 14th, 08:00).
    private const int CalendarYear = 1370, CalendarMonth = 2, CalendarDay = 14, CalendarStartSeconds = 8 * 3600;

    private ReadOnlySpan<byte> BuildEnvironment()
    {
        var e = _environment.AsSpan();
        e.Clear();
        e[0] = 1; e[3] = 1;
        var total = CalendarStartSeconds + _clock.ElapsedMilliseconds * CalendarTimeFactor / 1000;
        var days = total / 86400; var seconds = total % 86400;
        var day = CalendarDay - 1 + days; var month = CalendarMonth - 1 + day / 30; day %= 30;
        var year = CalendarYear + month / 12; month %= 12;
        BinaryPrimitives.WriteInt32LittleEndian(e[4..], (int)year);
        BinaryPrimitives.WriteUInt16LittleEndian(e[8..], (ushort)(month + 1));
        BinaryPrimitives.WriteUInt16LittleEndian(e[10..], (ushort)(day + 1));
        BinaryPrimitives.WriteUInt16LittleEndian(e[12..], (ushort)(seconds / 3600));
        BinaryPrimitives.WriteUInt16LittleEndian(e[14..], (ushort)(seconds / 60 % 60));
        BinaryPrimitives.WriteUInt16LittleEndian(e[16..], (ushort)(seconds % 60));
        return e;
    }

    private void SendEnvironment(PlayerSession s) => SendMessage(s, 0x00B, 0, 0, BuildEnvironment());

    /// <summary>Called from the tick: the periodic broadcast to every connected player.</summary>
    private void PumpEnvironment(long now)
    {
        if (now < _nextEnvironmentMs) return;
        _nextEnvironmentMs = now + EnvironmentIntervalMs;
        var environment = BuildEnvironment();
        for (var slot = 1; slot <= MaxSlots; slot++)
            if (_slots[slot] is { } p && p.State >= SessionState.SlotAssigned) SendMessage(p, 0x00B, 0, 0, environment);
    }
}
