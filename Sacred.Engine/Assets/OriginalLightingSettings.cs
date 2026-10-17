using System;
using System.Globalization;
using System.IO;

namespace Sacred.Engine.Assets;

internal static class OriginalLightingSettings
{
    public static float ReadNightDarkness(string gameDirectory)
    {
        var path = Path.Combine(gameDirectory, "Settings.cfg");
        if (!File.Exists(path)) return 0.5f;
        foreach (var line in File.ReadLines(path))
        {
            var separator = line.IndexOf(':');
            if (separator < 0 || !line.AsSpan(0, separator).Trim().Equals("NIGHT_DARKNESS", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!int.TryParse(line.AsSpan(separator + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent))
                return 0;
            // Native constructor resets an out-of-range value to zero.
            return percent >= 0 && percent <= 100 ? percent / 100f : 0;
        }
        return 0.5f;
    }
}
