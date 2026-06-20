using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sacred.ItemViewer.Avalonia.ItemViewer;

internal sealed class SacredItemFavoriteStore
{
    private const int CurrentVersion = 1;
    private const string ApplicationDirectoryName = "Sacred.ItemViewer.Avalonia";
    private const string FileName = "favorites.json";

    public SacredItemFavoriteStore(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    public static SacredItemFavoriteStore CreateDefault()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
            appData = AppContext.BaseDirectory;

        return new SacredItemFavoriteStore(Path.Combine(appData, ApplicationDirectoryName, FileName));
    }

    public HashSet<uint> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];

            var json = File.ReadAllText(FilePath);
            var data = JsonSerializer.Deserialize(
                json,
                SacredItemFavoriteJsonContext.Default.SacredItemFavoriteSaveData);

            return data?.Version == CurrentVersion
                ? data.ItemIds.ToHashSet()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public void Save(IReadOnlySet<uint> itemIds)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var data = new SacredItemFavoriteSaveData
            {
                Version = CurrentVersion,
                ItemIds = itemIds.Order().ToArray()
            };
            var json = JsonSerializer.Serialize(
                data,
                SacredItemFavoriteJsonContext.Default.SacredItemFavoriteSaveData);
            var temporaryPath = FilePath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, FilePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

internal sealed record SacredItemFavoriteSaveData
{
    public int Version { get; init; }

    public uint[] ItemIds { get; init; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SacredItemFavoriteSaveData))]
internal sealed partial class SacredItemFavoriteJsonContext : JsonSerializerContext;
