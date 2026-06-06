using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SacredItemSimulator.Avalonia.ItemViewer;

internal sealed class SacredItemFilterSaveStore
{
    private const int CurrentVersion = 1;
    private const string ApplicationDirectoryName = "SacredItemSimulator.Avalonia";
    private const string FileName = "filters.json";
    private const string DefaultGameDirectory = @"E:\SteamLibrary\steamapps\common\Sacred Gold";

    public SacredItemFilterSaveStore(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    public static SacredItemFilterSaveStore CreateDefault()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
            appData = AppContext.BaseDirectory;

        return new SacredItemFilterSaveStore(Path.Combine(appData, ApplicationDirectoryName, FileName));
    }

    public SacredItemFilterSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return CreateDefaultSettings();

            var json = File.ReadAllText(FilePath);
            var saveData = JsonSerializer.Deserialize(
                json,
                SacredItemFilterSaveJsonContext.Default.SacredItemFilterSaveData);

            if (saveData?.Version != CurrentVersion)
                return CreateDefaultSettings();

            var enumFilters = saveData.EnumFilters
                .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value.Length > 0)
                .ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value.ToHashSet(),
                    StringComparer.Ordinal);

            var gameDirectory = string.IsNullOrWhiteSpace(saveData.GameDirectory)
                ? DefaultGameDirectory
                : saveData.GameDirectory;

            return new SacredItemFilterSettings(
                enumFilters,
                saveData.FilterHasModel,
                gameDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return CreateDefaultSettings();
        }
    }

    public void Save(SacredItemFilterSettings settings)
    {
        try
        {
            var saveData = new SacredItemFilterSaveData
            {
                Version = CurrentVersion,
                EnumFilters = settings.EnumFilters
                    .Where(static pair => pair.Value.Count > 0)
                    .ToDictionary(
                        static pair => pair.Key,
                        static pair => pair.Value.Order().ToArray(),
                        StringComparer.Ordinal),
                FilterHasModel = settings.FilterHasModel,
                GameDirectory = settings.GameDirectory
            };

            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(
                saveData,
                SacredItemFilterSaveJsonContext.Default.SacredItemFilterSaveData);
            var temporaryPath = FilePath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, FilePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static SacredItemFilterSettings CreateDefaultSettings()
    {
        return new SacredItemFilterSettings([], false, DefaultGameDirectory);
    }
}

internal sealed record SacredItemFilterSettings(
    Dictionary<string, HashSet<ulong>> EnumFilters,
    bool FilterHasModel,
    string GameDirectory);

internal sealed record SacredItemFilterSaveData
{
    public int Version { get; init; }

    public Dictionary<string, ulong[]> EnumFilters { get; init; } = [];

    public bool FilterHasModel { get; init; }

    public string GameDirectory { get; init; } = "";
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SacredItemFilterSaveData))]
internal sealed partial class SacredItemFilterSaveJsonContext : JsonSerializerContext;
