using System;
using System.Numerics;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Gold weather-manager calendar and unlit surface colour calculations.</summary>
internal static class WorldDayNightLighting
{
    public const int MinutesPerDay = 1440;
    public const int DawnStart = 200;
    public const int DawnEnd = 320;
    public const int DuskStart = 1220;
    public const int DuskEnd = 1340;

    public static int GetNightValue(float dayFraction)
    {
        // Recover whole calendar minutes when their float fraction rounded down by
        // one ULP. Native UpdateNightFlag (0x41C2D0) does not use seconds.
        var minute = Math.Min((int)MathF.BitIncrement(dayFraction * MinutesPerDay), MinutesPerDay - 1);
        if (minute < DawnStart || minute > DuskEnd) return 255;
        if (minute <= DawnEnd) return (int)((DawnEnd - minute) * 2.125f);
        return minute < DuskStart ? 0 : (int)((minute - DuskStart) * 2.125f);
    }

    public static Vector3 GetSurfaceColour(int nightValue, float nightDarkness)
    {
        // Constructor 0x4188BC builds the night palettes; hilight_getColor_sc
        // (0x41B510) starts at intensity 32 away from emitters. Twilight ADDS
        // daylight to each palette channel, then clamps, rather than lerping.
        var paletteBase = 64 + nightDarkness * 64;
        var redGreen = (int)(32 * 0.25f + paletteBase);
        var blue = (int)(32 / 3.0f + paletteBase);
        var daylight = 255 - nightValue;
        return new Vector3(Math.Min(255, redGreen + daylight), Math.Min(255, redGreen + daylight),
            Math.Min(255, blue + daylight)) / 255f;
    }
}
