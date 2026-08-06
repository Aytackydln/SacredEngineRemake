#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using Sacred.Engine;
using Sacred.Engine.Latency;
using Sacred.Engine.Scene.InGame;

namespace SacredRemake;

internal static class SacredEngineRemakeConfig
{
    private const string FileName = "SacredEngineRemake.cfg";

    private const string HdrKey = "HDR";
    private const string BorderlessFullscreenKey = "BORDERLESS_FULLSCREEN";
    private const string WindowedWidthKey = "WINDOWED_WIDTH";
    private const string WindowedHeightKey = "WINDOWED_HEIGHT";
    private const string FramePacingKey = "FRAME_PACING";
    private const string LowLatencyKey = "LOW_LATENCY";
    private const string WorldLightingKey = "WORLD_LIGHTING";
    private const string StairsTilesKey = "STAIRS_TILES";
    private const string BlockedTilesKey = "BLOCKED_TILES";
    private const string CharacterKey = "CHARACTER";
    private const string LocationXKey = "LOCATION_X";
    private const string LocationYKey = "LOCATION_Y";

    public static SacredGameSaveState Load(string gameDirectory)
    {
        var path = Path.Combine(gameDirectory, FileName);
        if (!File.Exists(path))
        {
            Console.WriteLine($"Remake config not found; using defaults: {path}");
            return new SacredGameSaveState();
        }

        try
        {
            var values = ReadValues(path);
            Vector2? location = TryReadLocation(values, out var savedLocation)
                ? savedLocation
                : null;
            var state = new SacredGameSaveState
            {
                BorderlessFullscreen = ReadBoolean(values, BorderlessFullscreenKey, defaultValue: true),
                WindowedWidth = ReadPositiveInteger(values, WindowedWidthKey, 1600),
                WindowedHeight = ReadPositiveInteger(values, WindowedHeightKey, 900),
                HdrEnabled = ReadBoolean(values, HdrKey),
                FramePacingMode = ReadEnum(values, FramePacingKey, FramePacingMode.VariableRefreshRate),
                LowLatencyMode = ReadEnum(values, LowLatencyKey, LowLatencyMode.On),
                WorldLightingMode = ReadEnum(values, WorldLightingKey, WorldLightingMode.TimedDayNightCycle),
                StairsTilesVisible = ReadBoolean(values, StairsTilesKey),
                BlockedTilesVisible = ReadBoolean(values, BlockedTilesKey),
                CharacterName = ReadString(values, CharacterKey),
                LastLocation = location
            };

            Console.WriteLine($"Loaded remake config: {path}");
            return state;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Could not load remake config '{path}'; using defaults. {exception.Message}");
            return new SacredGameSaveState();
        }
    }

    public static void Save(string gameDirectory, SacredGameSaveState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var path = Path.Combine(gameDirectory, FileName);
        var temporaryPath = path + ".tmp";

        try
        {
            var lines = BuildLines(state);
            File.WriteAllLines(temporaryPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, path, overwrite: true);
            Console.WriteLine($"Saved remake config: {path}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Could not save remake config '{path}'. {exception.Message}");
        }
    }

    private static Dictionary<string, string> ReadValues(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(path))
        {
            var separator = line.IndexOf(':');
            if (separator < 0)
                continue;

            var name = line[..separator].Trim();
            if (name.Length == 0)
                continue;

            values[name] = line[(separator + 1)..].Trim();
        }

        return values;
    }

    private static string[] BuildLines(SacredGameSaveState state)
    {
        return
        [
            $"{BorderlessFullscreenKey} : {FormatBoolean(state.BorderlessFullscreen)}",
            $"{WindowedWidthKey} : {state.WindowedWidth}",
            $"{WindowedHeightKey} : {state.WindowedHeight}",
            $"{HdrKey} : {FormatBoolean(state.HdrEnabled)}",
            $"{FramePacingKey} : {state.FramePacingMode}",
            $"{LowLatencyKey} : {state.LowLatencyMode}",
            $"{WorldLightingKey} : {state.WorldLightingMode}",
            $"{StairsTilesKey} : {FormatBoolean(state.StairsTilesVisible)}",
            $"{BlockedTilesKey} : {FormatBoolean(state.BlockedTilesVisible)}",
            $"{CharacterKey} : {SanitizeLineValue(state.CharacterName)}",
            $"{LocationXKey} : {FormatLocationComponent(state.LastLocation?.X)}",
            $"{LocationYKey} : {FormatLocationComponent(state.LastLocation?.Y)}"
        ];
    }

    private static bool ReadBoolean(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) &&
        (value == "1" || bool.TryParse(value, out var parsed) && parsed);

    private static bool ReadBoolean(
        IReadOnlyDictionary<string, string> values,
        string key,
        bool defaultValue) =>
        values.ContainsKey(key) ? ReadBoolean(values, key) : defaultValue;

    private static int ReadPositiveInteger(
        IReadOnlyDictionary<string, string> values,
        string key,
        int fallback) =>
        values.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : fallback;

    private static string? ReadString(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static TEnum ReadEnum<TEnum>(
        IReadOnlyDictionary<string, string> values,
        string key,
        TEnum fallback)
        where TEnum : struct, Enum
    {
        return values.TryGetValue(key, out var value) &&
               Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) &&
               Enum.IsDefined(parsed)
            ? parsed
            : fallback;
    }

    private static bool TryReadLocation(
        IReadOnlyDictionary<string, string> values,
        out Vector2 location)
    {
        if (TryReadFiniteFloat(values, LocationXKey, out var x) &&
            TryReadFiniteFloat(values, LocationYKey, out var y))
        {
            location = new Vector2(x, y);
            return true;
        }

        location = default;
        return false;
    }

    private static bool TryReadFiniteFloat(
        IReadOnlyDictionary<string, string> values,
        string key,
        out float value)
    {
        value = default;
        return values.TryGetValue(key, out var text) &&
               float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
               float.IsFinite(value);
    }

    private static int FormatBoolean(bool value) => value ? 1 : 0;

    private static string FormatLocationComponent(float? value) =>
        value?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string SanitizeLineValue(string? value) =>
        (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
}
