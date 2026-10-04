using System;
using System.Globalization;
using Sacred.Engine.Extern;

namespace Sacred.Engine.Graphics;

public readonly record struct OsSdrWhiteLevelInfo(float Nits, string Source, bool IsAvailable);

/// <summary>Reads the window's SDR white without changing OS display settings.</summary>
public static class OsSdrWhiteLevel
{
    // Explicit launch override for compositors whose SDR white is not exposed through Wine.
    public const string OverrideVariable = "SACRED_SDR_WHITE_NITS";

    public static OsSdrWhiteLevelInfo Read(nint window = 0)
    {
        var configured = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (float.TryParse(configured, NumberStyles.Float, CultureInfo.InvariantCulture, out var nits) &&
                float.IsFinite(nits) && nits is >= 1 and <= 10_000)
                return new(nits, $"Launch override ({OverrideVariable})", true);
            return Unavailable($"Invalid {OverrideVariable}; expected 1–10000 nits");
        }

        try { return ReadDisplay(window); }
        catch (DllNotFoundException) { return Unavailable("OS display API unavailable"); }
        catch (EntryPointNotFoundException) { return Unavailable("OS SDR-white query unavailable"); }
    }

    private static OsSdrWhiteLevelInfo Unavailable(string reason) =>
        new(HdrBrightnessSettings.DefaultSceneBrightnessNits, reason, false);

    private static unsafe OsSdrWhiteLevelInfo ReadDisplay(nint window)
    {
        // Nearest monitor follows the game's window; a headless query uses the primary display.
        var monitor = DisplayConfig.MonitorFromWindow(window, window == 0 ? 1u : 2u);
        var info = new DisplayConfig.MonitorInfo { Size = (uint)sizeof(DisplayConfig.MonitorInfo) };
        if (monitor == 0 || DisplayConfig.GetMonitorInfo(monitor, &info) == 0)
            return Unavailable("Cannot identify the game display");
        var deviceName = new string(info.DeviceName);

        // Display topology can change between sizing and enumeration.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int error = DisplayConfig.GetBufferSizes(DisplayConfig.ActivePaths, out var pathCount, out var modeCount);
            if (error != 0) return Unavailable($"Display topology unavailable ({error})");
            if (pathCount == 0 || pathCount > 4096 || modeCount > 8192)
                return Unavailable("Invalid display topology");
            var paths = new DisplayConfig.PathInfo[pathCount];
            var modes = new DisplayConfig.ModeInfo[Math.Max(modeCount, 1)];
            fixed (DisplayConfig.PathInfo* pathData = paths)
            fixed (DisplayConfig.ModeInfo* modeData = modes)
                error = DisplayConfig.Query(DisplayConfig.ActivePaths, ref pathCount, pathData,
                    ref modeCount, modeData, 0);
            if (error == DisplayConfig.InsufficientBuffer) continue;
            if (error != 0) return Unavailable($"Display enumeration unavailable ({error})");

            for (int index = 0; index < pathCount; index++)
            {
                var path = paths[index];
                var source = new DisplayConfig.SourceDeviceName
                {
                    Header = new() { Type = 1, Size = (uint)sizeof(DisplayConfig.SourceDeviceName),
                        Adapter = path.SourceAdapter, Id = path.SourceId }
                };
                if (DisplayConfig.GetDeviceInfo(&source.Header) != 0 ||
                    !deviceName.Equals(new string(source.Name), StringComparison.OrdinalIgnoreCase)) continue;

                var white = new DisplayConfig.SdrWhiteLevel
                {
                    Header = new() { Type = 11, Size = (uint)sizeof(DisplayConfig.SdrWhiteLevel),
                        Adapter = path.TargetAdapter, Id = path.TargetId }
                };
                error = DisplayConfig.GetDeviceInfo(&white.Header);
                if (error != 0) return Unavailable($"SDR white unavailable on {deviceName} ({error})");
                float nits = white.Level * (80.0f / 1000.0f);
                if (!float.IsFinite(nits) || nits is < 1 or > 10_000)
                    return Unavailable("OS returned an invalid SDR white level");
                return new(nits, $"OS SDR white ({deviceName})", true);
            }
            return Unavailable("No active display path matches the game monitor");
        }
        return Unavailable("Display topology changed during the query");
    }
}
