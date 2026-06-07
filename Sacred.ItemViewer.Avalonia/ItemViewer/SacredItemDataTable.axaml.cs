using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Sacred.Assets;
using Sacred.Assets.Paks.Models;
using Sacred.Assets.Paks.Texture;
using Sacred.Core;
using Sacred.Core.Weapon;
using Sacred.Granny;

namespace Sacred.ItemViewer.Avalonia.ItemViewer;

public partial class SacredItemDataTable : UserControl
{
    private const string DefaultGameDir = @"E:\SteamLibrary\steamapps\common\Sacred Gold";

    private SacredItemDataTableViewModel _tableViewModel = new([], FrozenDictionary<string, string>.Empty);
    private readonly ModelViewerControl _modelViewer = new();
    private readonly SacredItemFilterSaveStore _filterSaveStore = SacredItemFilterSaveStore.CreateDefault();
    private readonly SacredItemPreviewConfirmationStore _previewConfirmationStore = SacredItemPreviewConfirmationStore.CreateDefault();
    private Dictionary<string, HashSet<ulong>> _savedEnumFilters = [];
    private IReadOnlyDictionary<uint, SacredItemPreviewConfirmation> _previewConfirmationsByItemId =
        new Dictionary<uint, SacredItemPreviewConfirmation>();
    private string _gameDir = DefaultGameDir;
    private ModelsPakArchive _modelsPakArchive = null!;
    private TexturePakArchive _texturePakArchive = null!;
    private CancellationTokenSource? _modelLoadCancellation;
    private SacredItemDataModel? _confirmablePreviewItem;
    private bool _hasLoaded;

    public SacredItemDataTable()
    {
        InitializeComponent();
        ModelViewerPanel.Children.Add(_modelViewer);
        PreviewRotationModeComboBox.ItemsSource = Enum.GetValues<ItemPreviewRotationMode>();
        PreviewRotationModeComboBox.SelectedItem = ItemPreviewRotationMode.LegacyCurrent;
        PreviewRotationModeComboBox.SelectionChanged += ExperimentModeComboBox_OnSelectionChanged;
        PreviewPivotModeComboBox.ItemsSource = Enum.GetValues<ItemPreviewPivotMode>();
        PreviewPivotModeComboBox.SelectedItem = ItemPreviewPivotMode.BoundsCenter;
        PreviewPivotModeComboBox.SelectionChanged += ExperimentModeComboBox_OnSelectionChanged;
        ModelYawSlider.ValueChanged += (_, _) => UpdateModelRotationFromSliders();
        ModelPitchSlider.ValueChanged += (_, _) => UpdateModelRotationFromSliders();
        ModelRollSlider.ValueChanged += (_, _) => UpdateModelRotationFromSliders();
        UpdateModelRotationFromSliders();
    }

    private void ResetModelRotationSliders()
    {
        SetModelRotationSliders(Vector3.Zero);
    }

    private void SetModelRotationSliders(Vector3 rotation)
    {
        ModelYawSlider.Value = rotation.X;
        ModelPitchSlider.Value = rotation.Y;
        ModelRollSlider.Value = rotation.Z;
        UpdateModelRotationFromSliders();
    }

    private void UpdateModelRotationFromSliders()
    {
        var yaw = ModelYawSlider.Value;
        var pitch = ModelPitchSlider.Value;
        var roll = ModelRollSlider.Value;

        ModelYawValueText.Text = FormatSliderRadians(yaw);
        ModelPitchValueText.Text = FormatSliderRadians(pitch);
        ModelRollValueText.Text = FormatSliderRadians(roll);

        _modelViewer.SetUserRotation(
            (float)yaw,
            (float)pitch,
            (float)roll);
    }

    private static string FormatSliderRadians(double radians)
    {
        return $"{radians:0.##} rad";
    }

    private async void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_hasLoaded)
            return;

        _hasLoaded = true;
        DataGrid.IsVisible = false;

        var savedSettings = _filterSaveStore.Load();
        _savedEnumFilters = savedSettings.EnumFilters;
        _previewConfirmationsByItemId = _previewConfirmationStore.LoadByItemId();
        _gameDir = await PromptForGameDirectoryAsync(savedSettings.GameDirectory);
        SaveSettings(savedSettings.FilterHasModel);

        var gameDirectories = CreateGameDirectories(_gameDir);
        var sacredGameData = SacredGameData.LoadFromGamePaks(gameDirectories);
        var items = sacredGameData.GamePakStore.Weapons.Values.ToList();

        _tableViewModel = new SacredItemDataTableViewModel(items, sacredGameData.GameResStore.TranslatedStrings);
        _tableViewModel.FilterHasModel = savedSettings.FilterHasModel;
        _tableViewModel.SetConfirmedPreviewItems(CreateConfirmedPreviewItems());
        _tableViewModel.FilterHasModelChanged += OnFilterHasModelChanged;
        DataContext = _tableViewModel;
        var pakDirectory = Path.Combine(_gameDir, "pak");
        _modelsPakArchive = ModelsPakArchive.Load(Path.Combine(pakDirectory, "models.pak"));
        _texturePakArchive = TexturePakArchive.LoadFromDirectory(pakDirectory);

        BuildEnumFilters();
        _tableViewModel.LoadPage(0);
        DataGrid.IsVisible = true;
    }

    private static SacredGameDirectories CreateGameDirectories(string gameDir)
    {
        return new SacredGameDirectories
        {
            GlobalResourcesPath = Path.Combine(gameDir, "scripts", "us", "global.res"),
            LocalResourcesPath = Path.Combine(gameDir, "scripts", "us", "SRglbl.res"),
            ReferenceResourcesPath = Path.Combine(gameDir, "scripts", "de", "SRglbl.res"),
            WeaponsPakPath = Path.Combine(gameDir, "pak", "Weapon.pak"),
            ItemsPakPath = Path.Combine(gameDir, "pak", "Items.pak"),
            TexturesPakPath = Path.Combine(gameDir, "pak", "texture.pak"),
        };
    }

    private async Task<string> PromptForGameDirectoryAsync(string savedGameDirectory)
    {
        var initialGameDirectory = string.IsNullOrWhiteSpace(savedGameDirectory)
            ? DefaultGameDir
            : savedGameDirectory;

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
            return initialGameDirectory;

        var dialog = new GameDirectoryPromptWindow(initialGameDirectory);
        var selectedGameDirectory = await dialog.ShowDialog<string?>(owner);

        return string.IsNullOrWhiteSpace(selectedGameDirectory)
            ? initialGameDirectory
            : selectedGameDirectory.Trim();
    }

    private void OnFilterHasModelChanged(bool value)
    {
        SaveSettings(value);
    }

    private void SaveSettings(bool filterHasModel)
    {
        _filterSaveStore.Save(new SacredItemFilterSettings(
            _savedEnumFilters,
            filterHasModel,
            _gameDir));
    }

    private void BuildEnumFilters()
    {
        var enumProperties = typeof(SacredItemDataModel)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => new
            {
                Property = property,
                EnumType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType
            })
            .Where(x => x.EnumType.IsEnum)
            .ToArray();

        if (enumProperties.Length == 0)
            return;

        var enumPropertyNames = enumProperties
            .Select(static property => property.Property.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var savedPropertyName in _savedEnumFilters.Keys.Where(name => !enumPropertyNames.Contains(name)).ToArray())
            _savedEnumFilters.Remove(savedPropertyName);

        var filters = enumProperties
            .Select(property =>
            {
                var isFlags = property.EnumType.GetCustomAttribute<FlagsAttribute>() is not null;
                _savedEnumFilters.TryGetValue(property.Property.Name, out var savedSelectedValues);
                return new EnumFilterViewModel(
                    property.Property.Name,
                    isFlags,
                    GetFilterValues(property.EnumType, isFlags),
                    savedSelectedValues,
                    filter => ApplyEnumFilter(filter, saveFilters: true));
            })
            .ToArray();

        _tableViewModel.SetEnumFilters(filters);

        foreach (var filter in filters)
        {
            if (filter.SelectedCount > 0)
                ApplyEnumFilter(filter, saveFilters: false);
            else
                _savedEnumFilters.Remove(filter.PropertyName);
        }
    }

    private void ApplyEnumFilter(
        EnumFilterViewModel filter,
        bool saveFilters)
    {
        var selectedNumericValues = filter.SelectedNumericValues
            .ToHashSet();

        _tableViewModel.SetEnumFilter(
            filter.PropertyName,
            filter.IsFlags,
            selectedNumericValues);

        if (selectedNumericValues.Count == 0)
            _savedEnumFilters.Remove(filter.PropertyName);
        else
            _savedEnumFilters[filter.PropertyName] = selectedNumericValues;

        if (saveFilters)
            SaveSettings(_tableViewModel.FilterHasModel);
    }

    private static IEnumerable<EnumFilterOption> GetFilterValues(Type enumType, bool isFlags)
    {
        var names = Enum.GetNames(enumType);
        var values = Enum.GetValuesAsUnderlyingType(enumType)
            .Cast<object>()
            .Select(Convert.ToUInt64)
            .Select((numericValue, index) => new EnumFilterOption(numericValue, names[index]));

        return isFlags
            ? values.Where(static value => IsZeroOrSingleBitFlag(value.NumericValue))
            : values;
    }

    private static bool IsZeroOrSingleBitFlag(ulong numericValue)
    {
        return numericValue == 0 || (numericValue & (numericValue - 1)) == 0;
    }

    private void DataGrid_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _modelViewer.ShowStatus("...");
        SetConfirmPreviewState(null, false, "");

        if (sender is not DataGrid { SelectedItem: SacredItemDataModel selectedItem })
        {
            ResetModelRotationSliders();
            _modelViewer.ShowStatus("Select an item to load its model.");
            return;
        }

        if (_previewConfirmationsByItemId.TryGetValue(selectedItem.ItemId, out var confirmation))
        {
            SetModelRotationSliders(ToVector3(confirmation.UserRotationYawPitchRoll));
        }

        if (string.IsNullOrWhiteSpace(selectedItem.ModelName))
        {
            _modelViewer.ShowStatus($"{selectedItem.ItemName}: no model name.");
            SetConfirmPreviewState(null, false, GetPreviewConfirmationStatus(selectedItem, "No model to confirm."));
            return;
        }

        SetConfirmPreviewState(null, false, GetPreviewConfirmationStatus(selectedItem, "Loading preview..."));
        var rotationMode = SelectedRotationMode;
        var pivotMode = SelectedPivotMode;
        _ = Task.Run(async () => await LoadModel(selectedItem, rotationMode, pivotMode));
    }

    private async Task LoadModel(
        SacredItemDataModel selectedItem,
        ItemPreviewRotationMode rotationMode,
        ItemPreviewPivotMode pivotMode)
    {
        if (_modelLoadCancellation != null)
        {
            await _modelLoadCancellation.CancelAsync();
            _modelLoadCancellation.Dispose();
            _modelLoadCancellation = null;
        }

        _modelLoadCancellation = new CancellationTokenSource();
        var cancellationToken = _modelLoadCancellation.Token;
        _modelViewer.ClearModel();
        _modelViewer.ShowStatus($"{selectedItem.ModelName}: loading...");

        try
        {
            var archive = _modelsPakArchive;
            var modelName = selectedItem.ModelName;
            var asset = await archive.LoadModelAsync(modelName, GrnMeshExtractionMode.PrimarySlice, cancellationToken);
            var viewerPreviewRotation = CreateViewerPreviewRotation(selectedItem, rotationMode);

            if (cancellationToken.IsCancellationRequested)
                return;

            _modelViewer.ShowModel(
                asset,
                viewerPreviewRotation,
                selectedItem.Width,
                selectedItem.Height,
                rotationMode,
                pivotMode);
            await _modelViewer.Dispatcher.InvokeAsync(UpdateModelRotationFromSliders);
            await _modelViewer.Dispatcher.InvokeAsync(() =>
            {
                if (DataGrid.SelectedItem is SacredItemDataModel currentItem && currentItem.ItemId == selectedItem.ItemId)
                {
                    var hasMesh = asset.Mesh is not null;
                    SetConfirmPreviewState(
                        hasMesh ? selectedItem : null,
                        hasMesh,
                        hasMesh
                            ? GetPreviewConfirmationStatus(selectedItem, "Ready to confirm preview rotation.")
                            : GetPreviewConfirmationStatus(selectedItem, "Loaded model has no mesh to confirm."));
                }
            });
            await LoadSelectedModelTexturesAsync(asset, selectedItem, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
        {
            _modelViewer.ShowStatus($"{selectedItem.ModelName}: {ex.Message}");
        }
    }

    private async Task LoadSelectedModelTexturesAsync(
        GrnAsset asset,
        SacredItemDataModel selectedItem,
        CancellationToken cancellationToken)
    {
        if (asset.Mesh is null)
            return;

        var textureNames = asset.Mesh.Surfaces
            .Select(static surface => surface.TextureName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var archive = _texturePakArchive;
        var loaded = new Dictionary<string, TextureAsset>(StringComparer.OrdinalIgnoreCase);
        var failedCount = 0;

        if (selectedItem.TextureId > 0 && textureNames.Length <= 1)
        {
            try
            {
                var itemTexture = await archive.LoadTextureAsync(selectedItem.TextureId, cancellationToken);
                loaded[textureNames.Length == 0 ? itemTexture.Name : textureNames[0]] = itemTexture;
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
            {
                failedCount++;
            }
        }

        if (textureNames.Length == 0 && loaded.Count == 0)
        {
            _modelViewer.ShowTextureStatus("no textures referenced");
            return;
        }

        var remainingTextureNames = textureNames
            .Where(textureName => !loaded.ContainsKey(textureName))
            .ToArray();

        _modelViewer.ShowTextureStatus($"loading {textureNames.Length + (selectedItem.TextureId > 0 && textureNames.Length <= 1 ? 1 : 0)} textures...");
        foreach (var textureName in remainingTextureNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                loaded[textureName] = await archive.LoadTextureAsync(textureName, cancellationToken);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
            {
                failedCount++;
            }
        }

        var result = new TextureLoadResult(loaded, failedCount);

        if (cancellationToken.IsCancellationRequested)
            return;

        _modelViewer.ShowTextures(result.Textures, result.FailedCount);
    }

    private void DataGrid_OnSorting(object? sender, DataGridColumnEventArgs e)
    {
        if (sender is not DataGrid dataGrid) return;

        var sortDescriptions = dataGrid.CollectionView.SortDescriptions;

        if (sortDescriptions.Count == 0)
        {
            _tableViewModel.Comparer = Comparer<SacredItemDataModel>.Create((x, y) => 0);
            return;
        }

        var comparer = CreateCustomComparer(sortDescriptions);
        _tableViewModel.Comparer = comparer;
    }

    private IComparer<SacredItemDataModel> CreateCustomComparer(DataGridSortDescriptionCollection sortDescriptions)
    {
        // Cache property getters for performance
        var getters = new List<(Func<SacredItemDataModel, object?> Getter, ListSortDirection Direction)>();

        foreach (var sortDesc in sortDescriptions)
        {
            var propInfo = typeof(SacredItemDataModel).GetProperty(sortDesc.PropertyPath);
            if (propInfo == null) continue;

            // Create a compiled getter (much faster than reflection every time)
            var parameter = Expression.Parameter(typeof(SacredItemDataModel), "x");
            var property = Expression.Property(parameter, propInfo);
            var converted = Expression.Convert(property, typeof(object));
            var getter = Expression.Lambda<Func<SacredItemDataModel, object?>>(converted, parameter)
                .Compile();

            getters.Add((getter, sortDesc.Direction));
        }

        return Comparer<SacredItemDataModel>.Create((x, y) =>
        {
            foreach (var (getter, direction) in getters)
            {
                var valueX = getter(x);
                var valueY = getter(y);

                int comparisonResult;

                if (valueX is IComparable comparableX && valueY is IComparable comparableY)
                {
                    comparisonResult = comparableX.CompareTo(comparableY);
                }
                else if (valueX == null && valueY == null)
                {
                    comparisonResult = 0;
                }
                else if (valueX == null)
                {
                    comparisonResult = -1;
                }
                else if (valueY == null)
                {
                    comparisonResult = 1;
                }
                else
                {
                    comparisonResult = 0;
                }

                if (comparisonResult != 0)
                {
                    return direction == ListSortDirection.Ascending
                        ? -comparisonResult
                        : comparisonResult;
                }
            }

            return 0;
        });
    }

    private void FirstPageButton_Click(object? sender, RoutedEventArgs e)
    {
        _tableViewModel.LoadPage(0);
    }

    private void LastPageButton_Click(object? sender, RoutedEventArgs e)
    {
        _tableViewModel.LoadPage(_tableViewModel.TotalPages - 1);
    }

    private sealed record TextureLoadResult(IReadOnlyDictionary<string, TextureAsset> Textures, int FailedCount);

    private ItemPreviewRotationMode SelectedRotationMode =>
        PreviewRotationModeComboBox.SelectedItem is ItemPreviewRotationMode mode
            ? mode
            : ItemPreviewRotationMode.LegacyCurrent;

    private ItemPreviewPivotMode SelectedPivotMode =>
        PreviewPivotModeComboBox.SelectedItem is ItemPreviewPivotMode mode
            ? mode
            : ItemPreviewPivotMode.BoundsCenter;

    private void ExperimentModeComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_hasLoaded || DataGrid.SelectedItem is not SacredItemDataModel selectedItem)
            return;

        if (string.IsNullOrWhiteSpace(selectedItem.ModelName))
            return;

        SetConfirmPreviewState(null, false, GetPreviewConfirmationStatus(selectedItem, "Loading preview..."));
        var rotationMode = SelectedRotationMode;
        var pivotMode = SelectedPivotMode;
        _ = Task.Run(async () => await LoadModel(selectedItem, rotationMode, pivotMode));
    }

    private static Vector3 CreateViewerPreviewRotation(
        SacredItemDataModel item,
        ItemPreviewRotationMode rotationMode)
    {
        return rotationMode == ItemPreviewRotationMode.LegacyCurrent
            ? CreateLegacyViewerPreviewRotation(item)
            : NormalizeRotation(item.PreviewRotation);
    }

    private static Vector3 CreateLegacyViewerPreviewRotation(SacredItemDataModel item)
    {
        if (item.EquipmentType == SacredEquipmentType.Shield)
            return CreateShieldPreviewRotation(item);

        var rotation = CanonicalizePreviewRotation(item.PreviewRotation);

        // Weapon.pak armor rotations are authored in direct yaw/pitch/roll order, while weapon entries
        // use the legacy item-viewer order that is already handled by Dx12ItemModelRenderer.
        return IsArmorEquipment(item.EquipmentType)
            ? new Vector3(rotation.Z, rotation.X, rotation.Y)
            : rotation;
    }

    private static Vector3 CreateShieldPreviewRotation(SacredItemDataModel item)
    {
        var rotation = NormalizeRotation(item.PreviewRotation);
        return item.ModelName.Equals("SHIELD_ROUND.GRN", StringComparison.OrdinalIgnoreCase)
            ? rotation with { X = NormalizeAngle(rotation.X + MathF.PI * 0.5f) }
            : rotation;
    }

    private static Vector3 CanonicalizePreviewRotation(Vector3 rotation)
    {
        var x = rotation.X;
        var y = rotation.Y;
        while (y > MathF.PI)
        {
            x -= MathF.PI;
            y -= MathF.PI;
        }

        while (y < -MathF.PI)
        {
            x += MathF.PI;
            y += MathF.PI;
        }

        return new Vector3(
            NormalizeAngle(x),
            NormalizeAngle(y),
            NormalizeAngle(rotation.Z));
    }

    private static Vector3 NormalizeRotation(Vector3 rotation)
    {
        return new Vector3(
            NormalizeAngle(rotation.X),
            NormalizeAngle(rotation.Y),
            NormalizeAngle(rotation.Z));
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle > MathF.PI)
            angle -= MathF.Tau;
        while (angle < -MathF.PI)
            angle += MathF.Tau;
        return angle;
    }

    private static bool IsArmorEquipment(SacredEquipmentType equipmentType)
    {
        return equipmentType is SacredEquipmentType.ChestArmor
            or SacredEquipmentType.HeadArmor
            or SacredEquipmentType.ArmArmor
            or SacredEquipmentType.LegArmor
            or SacredEquipmentType.Belt
            or SacredEquipmentType.Shoulder
            or SacredEquipmentType.FootArmor
            or SacredEquipmentType.Gloves;
    }

    private void ModelYawResetButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ModelYawSlider.Value = 0.0;
    }

    private void ModelPitchResetButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ModelPitchSlider.Value = 0.0;
    }

    private void ModelRollResetButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ModelRollSlider.Value = 0.0;
    }

    private void ConfirmPreviewButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_confirmablePreviewItem is not { } item)
        {
            SetConfirmPreviewState(null, false, "Load a model before confirming.");
            return;
        }

        var rotationMode = SelectedRotationMode;
        var pivotMode = SelectedPivotMode;
        var viewerPreviewRotation = CreateViewerPreviewRotation(item, rotationMode);
        var userRotationYawPitchRoll = new Vector3(
            (float)ModelYawSlider.Value,
            (float)ModelPitchSlider.Value,
            (float)ModelRollSlider.Value);
        var confirmation = SacredItemPreviewConfirmation.Create(
            item,
            viewerPreviewRotation,
            userRotationYawPitchRoll,
            rotationMode,
            pivotMode,
            DateTimeOffset.Now);

        var saved = _previewConfirmationStore.Save(confirmation);
        if (saved)
        {
            var confirmations = new Dictionary<uint, SacredItemPreviewConfirmation>(_previewConfirmationsByItemId)
            {
                [item.ItemId] = confirmation
            };
            _previewConfirmationsByItemId = confirmations;
            _tableViewModel.SetConfirmedPreviewItems(CreateConfirmedPreviewItems());
            ConfirmPreviewStatusText.Text = $"Saved {item.ItemId} to {Path.GetFileName(_previewConfirmationStore.FilePath)}; user rot {FormatRotationDegrees(confirmation.UserRotationYawPitchRollDegrees)}.";
        }
        else
        {
            ConfirmPreviewStatusText.Text = $"Could not save {Path.GetFileName(_previewConfirmationStore.FilePath)}";
        }
    }

    private void SetConfirmPreviewState(SacredItemDataModel? item, bool enabled, string status)
    {
        _confirmablePreviewItem = item;
        ConfirmPreviewButton.IsEnabled = enabled;
        ConfirmPreviewStatusText.Text = status;
    }

    private IReadOnlyDictionary<uint, DateTimeOffset> CreateConfirmedPreviewItems()
    {
        return _previewConfirmationsByItemId.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ConfirmedAt);
    }

    private string GetPreviewConfirmationStatus(SacredItemDataModel item, string fallback)
    {
        return _previewConfirmationsByItemId.TryGetValue(item.ItemId, out var confirmation)
            ? $"Previously confirmed {confirmation.ConfirmedAt:yyyy-MM-dd HH:mm}; {confirmation.RotationMode}/{confirmation.PivotMode}; saved user rot {FormatRotationDegrees(confirmation.UserRotationYawPitchRollDegrees)}. Confirm again to update."
            : fallback;
    }

    private static Vector3 ToVector3(RotationVectorData rotation)
    {
        return new Vector3(rotation.X, rotation.Y, rotation.Z);
    }

    private static string FormatRotationDegrees(RotationVectorData rotation)
    {
        return $"yaw {rotation.X:0.#}, pitch {rotation.Y:0.#}, roll {rotation.Z:0.#} deg";
    }

}
