using System;
using System.Globalization;

namespace Sacred.Engine.Scene.InGame;

internal static class WorldLightingCheats
{
    public static bool TryExecute(string option, string value, WorldLightingController controller,
        SceneLighting lighting, out string message)
    {
        message = string.Empty;
        if (!option.Equals("lighting-time", StringComparison.OrdinalIgnoreCase)) return false;
        if (value.Equals("live", StringComparison.OrdinalIgnoreCase)) controller.SetDebugDayFraction(null);
        else if (!value.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            if (!TimeSpan.TryParseExact(value, [@"hh\:mm", @"hh\:mm\:ss"], CultureInfo.InvariantCulture, out var time) ||
                time.TotalDays >= 1)
            {
                message = "lighting-time expects HH:mm[:ss], live or status";
                return true;
            }
            controller.SetDebugDayFraction((float)time.TotalDays);
        }
        controller.Update(0, lighting, controller.CurrentZone);
        var colour = lighting.WorldSurfaceAmbientColour * 255;
        message = $"lighting {FormatTime(controller.DayFraction)}; {controller.DisplayName}; " +
                  $"night value {lighting.NightBlend * 255:0}; surface RGB {colour.X:0},{colour.Y:0},{colour.Z:0}; " +
                  $"outdoor shadow {lighting.OutdoorShadowOpacity:0.000}";
        return true;
    }

    private static string FormatTime(float fraction) => TimeSpan.FromSeconds(Math.Round(fraction * 86400)).ToString(@"hh\:mm\:ss");
}
