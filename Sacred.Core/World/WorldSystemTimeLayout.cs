using System.Runtime.InteropServices;

namespace Sacred.Core.World;

/// <summary>Eight UInt16 fields of the native Windows SYSTEMTIME stored in sector metadata.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct WorldSystemTimeLayout
{
    public readonly ushort Year;
    public readonly ushort Month;
    public readonly ushort DayOfWeek;
    public readonly ushort Day;
    public readonly ushort Hour;
    public readonly ushort Minute;
    public readonly ushort Second;
    public readonly ushort Milliseconds;
}
