using System.Runtime.InteropServices;

namespace Sacred.Core.World.Sector;

/// <summary>Native cSectorEnvironment::cSectorWeather; four packed entries per environment.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SectorWeatherLayout
{
    public const int SerializedSize = 0x1C;

    /// <summary>Authored weather selector; values use Sacred's weather event vocabulary.</summary>
    [FieldOffset(0x00)] public readonly SectorWeatherKind Type;
    /// <summary>Native time[0]. Units and range interpretation are not established here.</summary>
    [FieldOffset(0x02)] public readonly uint Time0;
    /// <summary>Native time[1].</summary>
    [FieldOffset(0x06)] public readonly uint Time1;
    /// <summary>Native duration[0].</summary>
    [FieldOffset(0x0A)] public readonly uint Duration0;
    /// <summary>Native duration[1].</summary>
    [FieldOffset(0x0E)] public readonly uint Duration1;
    [FieldOffset(0x12)] public readonly byte Intensity0;
    [FieldOffset(0x13)] public readonly byte Intensity1;
    [FieldOffset(0x14)] public readonly uint Parameter0;
    [FieldOffset(0x18)] public readonly uint Parameter1;
}
