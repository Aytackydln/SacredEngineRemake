using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sacred.Core.Pak.Weapon;

namespace Sacred.ItemViewer.Avalonia.ItemViewer;

internal sealed class SacredItemPreviewConfirmationStore(string filePath)
{
    private const int CurrentVersion = 1;
    private const string ApplicationDirectoryName = "Sacred.ItemViewer.Avalonia";
    private const string FileName = "confirmed-preview-rotations.json";

    public string FilePath { get; } = filePath;

    public static SacredItemPreviewConfirmationStore CreateDefault()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
            appData = AppContext.BaseDirectory;

        return new SacredItemPreviewConfirmationStore(Path.Combine(appData, ApplicationDirectoryName, FileName));
    }

    public bool Save(SacredItemPreviewConfirmation confirmation)
    {
        try
        {
            var current = LoadSaveDataOrDefault();
            var entries = current.Entries
                .Where(entry => entry.ItemId != confirmation.ItemId)
                .Append(confirmation)
                .OrderBy(entry => entry.ItemId)
                .ToArray();

            var saveData = new SacredItemPreviewConfirmationSaveData
            {
                Version = CurrentVersion,
                Entries = entries
            };

            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(
                saveData,
                SacredItemPreviewConfirmationJsonContext.Default.SacredItemPreviewConfirmationSaveData);
            var temporaryPath = FilePath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, FilePath, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    public IReadOnlyDictionary<uint, SacredItemPreviewConfirmation> LoadByItemId()
    {
        return LoadSaveDataOrDefault()
            .Entries
            .GroupBy(static entry => entry.ItemId)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderByDescending(entry => entry.ConfirmedAt).First());
    }

    private SacredItemPreviewConfirmationSaveData LoadSaveDataOrDefault()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new SacredItemPreviewConfirmationSaveData { Version = CurrentVersion };

            var json = File.ReadAllText(FilePath);
            var saveData = JsonSerializer.Deserialize(
                json,
                SacredItemPreviewConfirmationJsonContext.Default.SacredItemPreviewConfirmationSaveData);

            return saveData?.Version == CurrentVersion
                ? saveData
                : new SacredItemPreviewConfirmationSaveData { Version = CurrentVersion };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new SacredItemPreviewConfirmationSaveData { Version = CurrentVersion };
        }
    }
}

internal sealed record SacredItemPreviewConfirmation
{
    public uint ItemId { get; init; }

    public string ItemName { get; init; } = "";

    public SacredCharacterClassMask CharacterClassMask { get; init; }

    public SacredEquipmentType EquipmentType { get; init; }

    public string ModelName { get; init; } = "";

    public uint TextureId { get; init; }

    public byte Width { get; init; }

    public byte Height { get; init; }

    public RotationVectorData RawPreviewRotation { get; init; }

    public RotationVectorData ViewerPreviewRotation { get; init; }

    public RotationVectorData RendererPreviewYawPitchRoll { get; init; }

    public RotationVectorData UserRotationYawPitchRoll { get; init; }

    public RotationVectorData UserRotationYawPitchRollDegrees { get; init; }

    public ItemPreviewRotationMode RotationMode { get; init; }

    public ItemPreviewPivotMode PivotMode { get; init; }

    public string? PivotBoneName { get; init; }

    public DateTimeOffset ConfirmedAt { get; init; }

    public static SacredItemPreviewConfirmation Create(
        SacredItemDataModel item,
        Vector3 viewerPreviewRotation,
        Vector3 userRotationYawPitchRoll,
        ItemPreviewRotationMode rotationMode,
        ItemPreviewPivotMode pivotMode,
        string? pivotBoneName,
        DateTimeOffset confirmedAt)
    {
        return new SacredItemPreviewConfirmation
        {
            ItemId = item.ItemId,
            ItemName = item.ItemName,
            CharacterClassMask = item.CharacterClassMask,
            EquipmentType = item.EquipmentType,
            ModelName = item.ModelName,
            TextureId = item.TextureId,
            Width = item.Width,
            Height = item.Height,
            RawPreviewRotation = RotationVectorData.FromVector(item.PreviewRotation),
            ViewerPreviewRotation = RotationVectorData.FromVector(viewerPreviewRotation),
            RendererPreviewYawPitchRoll = new RotationVectorData(
                viewerPreviewRotation.Y,
                viewerPreviewRotation.Z,
                viewerPreviewRotation.X),
            UserRotationYawPitchRoll = RotationVectorData.FromVector(userRotationYawPitchRoll),
            UserRotationYawPitchRollDegrees = RotationVectorData.FromVector(RadiansToDegrees(userRotationYawPitchRoll)),
            RotationMode = rotationMode,
            PivotMode = pivotMode,
            PivotBoneName = pivotBoneName,
            ConfirmedAt = confirmedAt
        };
    }

    private static Vector3 RadiansToDegrees(Vector3 radians)
    {
        return radians * (180.0f / MathF.PI);
    }
}

internal readonly record struct RotationVectorData(float X, float Y, float Z)
{
    public static RotationVectorData FromVector(Vector3 vector)
    {
        return new RotationVectorData(vector.X, vector.Y, vector.Z);
    }
}

internal sealed record SacredItemPreviewConfirmationSaveData
{
    public int Version { get; init; }

    public SacredItemPreviewConfirmation[] Entries { get; init; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SacredItemPreviewConfirmationSaveData))]
internal sealed partial class SacredItemPreviewConfirmationJsonContext : JsonSerializerContext;
