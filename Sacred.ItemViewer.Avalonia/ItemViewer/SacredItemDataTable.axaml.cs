using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Sacred.Assets;
using Sacred.Assets.Paks.Models;
using Sacred.Assets.Paks.Texture;
using Sacred.Core;
using Sacred.Core.GameRes;
using Sacred.Core.Pak.Weapon;
using Sacred.Granny.Abstractions;
using Sacred.Granny.Assets;
using Sacred.Granny.Loading;

namespace Sacred.ItemViewer.Avalonia.ItemViewer;

public partial class SacredItemDataTable : UserControl
{
    private const string DefaultGameDir = @"E:\SteamLibrary\steamapps\common\Sacred Gold";

    private SacredItemDataTableViewModel _tableViewModel = new([], GameResStore.Empty);
    private readonly ModelViewerControl _modelViewer = new();
    private readonly SacredItemFilterSaveStore _filterSaveStore = SacredItemFilterSaveStore.CreateDefault();
    private readonly SacredItemFavoriteStore _favoriteStore = SacredItemFavoriteStore.CreateDefault();
    private readonly SacredItemPreviewConfirmationStore _previewConfirmationStore = SacredItemPreviewConfirmationStore.CreateDefault();
    private Dictionary<string, HashSet<ulong>> _savedEnumFilters = [];
    private HashSet<uint> _favoriteItemIds = [];
    private IReadOnlyDictionary<uint, SacredItemPreviewConfirmation> _previewConfirmationsByItemId =
        new Dictionary<uint, SacredItemPreviewConfirmation>();
    private string? _selectedPivotBoneName;
    private bool _updatingBoneSelector;
    private string _gameDir = DefaultGameDir;
    private ModelsPakArchive _modelsPakArchive = null!;
    private TexturePakArchive _texturePakArchive = null!;
    private ItemSelectionSoundPlayer? _itemSelectionSoundPlayer;
    private CancellationTokenSource? _modelLoadCancellation;
    private SacredItemDataModel? _confirmablePreviewItem;
    private GrnBackendKind _grannyBackend = GrnBackendKind.ManagedParser;
    private bool _changingGrannyBackend;
    private bool _hasLoaded;

    public SacredItemDataTable()
    {
        InitializeComponent();
        GrannyBackendComboBox.ItemsSource = Enum.GetValues<GrnBackendKind>();
        GrannyBackendComboBox.SelectedItem = _grannyBackend;
        ModelViewerPanel.Children.Add(_modelViewer);
        PreviewRotationModeComboBox.ItemsSource = ItemPreviewRotationModeFactory.GetValues();
        PreviewRotationModeComboBox.SelectedItem = ItemPreviewRotationMode.LegacyCurrent;
        PreviewRotationModeComboBox.SelectionChanged += ExperimentModeComboBox_OnSelectionChanged;
        PreviewPivotModeComboBox.ItemsSource = ItemPreviewPivotModeFactory.GetValues();
        PreviewPivotModeComboBox.SelectedItem = ItemPreviewPivotMode.BoundsCenter;
        PreviewPivotModeComboBox.SelectionChanged += ExperimentModeComboBox_OnSelectionChanged;
        ModelYawSlider.ValueChanged += (_, _) => UpdateModelRotationFromSliders();
        ModelPitchSlider.ValueChanged += (_, _) => UpdateModelRotationFromSliders();
        ModelRollSlider.ValueChanged += (_, _) => UpdateModelRotationFromSliders();
        DetachedFromVisualTree += (_, _) =>
        {
            _itemSelectionSoundPlayer?.Dispose();
            _itemSelectionSoundPlayer = null;
        };
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
        _favoriteItemIds = _favoriteStore.Load();
        _previewConfirmationsByItemId = _previewConfirmationStore.LoadByItemId();
        _grannyBackend = savedSettings.GrannyBackend;
        _changingGrannyBackend = true;
        try
        {
            GrannyBackendComboBox.SelectedItem = _grannyBackend;
        }
        finally
        {
            _changingGrannyBackend = false;
        }
        _gameDir = await PromptForGameDirectoryAsync(savedSettings.GameDirectory);
        SaveSettings(savedSettings.FilterHasModel);

        var gameDirectories = CreateGameDirectories(_gameDir);
        var sacredGameData = SacredGameData.LoadFromGamePaks(gameDirectories);
        var items = sacredGameData.GamePakStore.Weapons.Values.ToList();

        _tableViewModel = new SacredItemDataTableViewModel(items, sacredGameData.GameResStore);
        _tableViewModel.FilterHasModel = savedSettings.FilterHasModel;
        _tableViewModel.SetFavoriteItems(_favoriteItemIds);
        _tableViewModel.SetConfirmedPreviewItems(CreateConfirmedPreviewItems());
        _tableViewModel.FilterHasModelChanged += OnFilterHasModelChanged;
        DataContext = _tableViewModel;
        var pakDirectory = Path.Combine(_gameDir, "pak");
        _modelsPakArchive = ModelsPakArchive.Load(
            Path.Combine(pakDirectory, "models.pak"),
            assetLoader: GrnAssetLoaderFactory.Create(_grannyBackend, _gameDir));
        _texturePakArchive = TexturePakArchive.LoadFromDirectory(pakDirectory);
        _itemSelectionSoundPlayer = new ItemSelectionSoundPlayer(Path.Combine(pakDirectory, "sound.pak"));
        _itemSelectionSoundPlayer.SetMuted(MuteSoundsCheckBox.IsChecked == true);

        BuildEnumFilters();
        _tableViewModel.LoadPage(0);
        DataGrid.IsVisible = true;
    }

    private async void GrannyBackendComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_changingGrannyBackend ||
            GrannyBackendComboBox.SelectedItem is not GrnBackendKind selectedBackend ||
            selectedBackend == _grannyBackend)
            return;

        var previousBackend = _grannyBackend;
        if (!_hasLoaded || _modelsPakArchive is null)
        {
            _grannyBackend = selectedBackend;
            return;
        }

        try
        {
            if (_modelLoadCancellation is not null)
                await _modelLoadCancellation.CancelAsync();
            var loader = GrnAssetLoaderFactory.Create(selectedBackend, _gameDir);
            _modelsPakArchive.ReplaceAssetLoader(loader);
            _grannyBackend = selectedBackend;
            SaveSettings(_tableViewModel.FilterHasModel);
            Console.WriteLine($"ItemViewer Granny implementation switched to {loader.DisplayName}.");

            if (DataGrid.SelectedItem is SacredItemDataModel selectedItem &&
                !string.IsNullOrWhiteSpace(selectedItem.ModelName))
            {
                _modelViewer.ShowStatus($"{selectedItem.ModelName}: reloading with {loader.DisplayName}...");
                var rotationMode = SelectedRotationMode;
                var pivotMode = SelectedPivotMode;
                _ = Task.Run(() => LoadModel(selectedItem, rotationMode, pivotMode));
            }
            else
            {
                _modelViewer.ShowStatus($"Granny implementation: {loader.DisplayName}.");
            }
        }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException or NotSupportedException)
        {
            _changingGrannyBackend = true;
            try
            {
                GrannyBackendComboBox.SelectedItem = previousBackend;
            }
            finally
            {
                _changingGrannyBackend = false;
            }
            _modelViewer.ShowStatus($"Could not select {selectedBackend}: {exception.Message}");
        }
    }

    private static SacredGameDirectories CreateGameDirectories(string gameDir)
    {
        return new SacredGameDirectories
        {
            GlobalResourcesPath = Path.Combine(gameDir, "scripts", "us", "global.res"),
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
            _gameDir,
            _grannyBackend));
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
        ClearModelData("no model selected");
        SetConfirmPreviewState(null, false, "");

        if (sender is not DataGrid { SelectedItem: SacredItemDataModel selectedItem })
        {
            ResetModelRotationSliders();
            _modelViewer.ShowStatus("Select an item to load its model.");
            return;
        }

        try
        {
            _itemSelectionSoundPlayer?.Play(selectedItem.Category);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or KeyNotFoundException)
        {
            Console.WriteLine($"Could not load the selected item's inventory sound: {exception.Message}");
        }

        if (_previewConfirmationsByItemId.TryGetValue(selectedItem.ItemId, out var confirmation))
        {
            SetModelRotationSliders(ToVector3(confirmation.UserRotationYawPitchRoll));
        }
        else
        {
            ResetModelRotationSliders();
        }

        if (string.IsNullOrWhiteSpace(selectedItem.ModelName))
        {
            _modelViewer.ShowStatus($"{selectedItem.ItemName}: no model name.");
            ClearModelData("item has no model");
            SetConfirmPreviewState(null, false, GetPreviewConfirmationStatus(selectedItem, "No model to confirm."));
            return;
        }

        SetConfirmPreviewState(null, false, GetPreviewConfirmationStatus(selectedItem, "Loading preview..."));
        var rotationMode = SelectedRotationMode;
        var pivotMode = SelectedPivotMode;
        _ = Task.Run(async () => await LoadModel(selectedItem, rotationMode, pivotMode));
    }

    private void MuteSoundsCheckBox_OnCheckedChanged(object? sender, RoutedEventArgs e)
    {
        _itemSelectionSoundPlayer?.SetMuted(MuteSoundsCheckBox.IsChecked == true);
    }

    private void DataGrid_OnAutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (e.PropertyName is nameof(SacredItemDataModel.IsFavorite)
            or nameof(SacredItemDataModel.FavoriteDisplay)
            or nameof(SacredItemDataModel.PreviewConfirmedDisplay)
            or nameof(SacredItemDataModel.PreviewConfirmedUserRotationIsZero))
        {
            e.Column.IsVisible = false;
            return;
        }

        if (e.PropertyName != nameof(SacredItemDataModel.PreviewConfirmed))
            return;

        e.Column = new DataGridTextColumn
        {
            Header = nameof(SacredItemDataModel.PreviewConfirmed),
            Binding = new Binding(nameof(SacredItemDataModel.PreviewConfirmedDisplay)),
            SortMemberPath = nameof(SacredItemDataModel.PreviewConfirmed),
            Width = new DataGridLength(120)
        };
    }

    private void FavoriteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SacredItemDataModel item })
            return;

        if (!_favoriteItemIds.Add(item.ItemId))
            _favoriteItemIds.Remove(item.ItemId);

        _favoriteStore.Save(_favoriteItemIds);
        _tableViewModel.SetFavoriteItems(_favoriteItemIds);
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
            var viewerPreviewRotation = selectedItem.PreviewRotation;
            var effectiveRotationMode = ResolveRotationMode(selectedItem, rotationMode);
            var availableBoneNames = GetBoneNames(asset.Diagnostics);
            if (_previewConfirmationsByItemId.TryGetValue(selectedItem.ItemId, out var savedConfirmation) &&
                !string.IsNullOrWhiteSpace(savedConfirmation.PivotBoneName))
                _selectedPivotBoneName = savedConfirmation.PivotBoneName;
            if (_selectedPivotBoneName is null ||
                !availableBoneNames.Contains(_selectedPivotBoneName, StringComparer.OrdinalIgnoreCase))
                _selectedPivotBoneName = availableBoneNames.FirstOrDefault();

            if (cancellationToken.IsCancellationRequested)
                return;

            var effectScene = EquipmentEffectSceneFactory.Create(asset, selectedItem.Damage) ?? EquipmentEffectScene.Empty;

            _modelViewer.ShowModel(
                asset,
                viewerPreviewRotation,
                selectedItem.Width,
                selectedItem.Height,
                effectiveRotationMode,
                pivotMode,
                _selectedPivotBoneName,
                effectScene);
            await _modelViewer.Dispatcher.InvokeAsync(() => ShowModelData(asset, selectedItem, effectScene));
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
            await LoadSelectedModelTexturesAsync(asset, selectedItem, effectScene, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
        {
            _modelViewer.ShowStatus($"{selectedItem.ModelName}: {ex.Message}");
            await _modelViewer.Dispatcher.InvokeAsync(() => ClearModelData("model failed to load"));
        }
    }

    private void ClearModelData(string status)
    {
        ModelDataExpander.Header = $"Model data — {status}";
        ModelDataExpander.IsEnabled = false;
        ModelDataText.Text = "";
    }

    private void ShowModelData(
        GrnAsset asset,
        SacredItemDataModel item,
        EquipmentEffectScene effectScene)
    {
        var diagnostics = asset.Diagnostics;
        if (diagnostics is null)
        {
            ClearModelData("metadata unavailable");
            return;
        }

        ModelDataExpander.Header =
            $"Model data — {diagnostics.Slices.Count} slices, {diagnostics.PartCount} parts, {diagnostics.BoneCount} bones";
        ModelDataExpander.IsEnabled = true;
        var boneNames = GetBoneNames(diagnostics);
        _updatingBoneSelector = true;
        try
        {
            PreviewPivotBoneComboBox.ItemsSource = boneNames;
            if (_selectedPivotBoneName is not null && boneNames.Contains(_selectedPivotBoneName, StringComparer.OrdinalIgnoreCase))
                PreviewPivotBoneComboBox.SelectedItem = boneNames.First(name => name.Equals(_selectedPivotBoneName, StringComparison.OrdinalIgnoreCase));
            else
                PreviewPivotBoneComboBox.SelectedIndex = boneNames.Length > 0 ? 0 : -1;
        }
        finally
        {
            _updatingBoneSelector = false;
        }

        var text = new StringBuilder();
        text.AppendLine($"Name: {asset.Name}");
        text.AppendLine($"Granny implementation: {asset.Backend}" +
                        (string.IsNullOrWhiteSpace(asset.BackendDetail) ? string.Empty : $" — {asset.BackendDetail}"));
        text.AppendLine($"File size: {asset.RawBytes.Length:N0} bytes");
        text.AppendLine($"Damage: physical {FormatRange(item.Damage.Physical)}, fire {FormatRange(item.Damage.Fire)}, magic {FormatRange(item.Damage.Magic)}, poison {FormatRange(item.Damage.Poison)}");
        var effectAnchors = diagnostics.Slices
            .SelectMany(static slice => slice.Bones)
            .Select(static bone => SacredEquipmentEffectAnchor.TryParse(bone.Name, out var anchor) ? anchor : (SacredEquipmentEffectAnchor?)null)
            .Where(static anchor => anchor.HasValue)
            .Select(static anchor => anchor!.Value)
            .DistinctBy(static anchor => anchor.BoneName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        text.AppendLine(effectAnchors.Length == 0
            ? "Equipped effects: none"
            : $"Equipped effects: {string.Join(", ", effectAnchors.Select(static anchor => $"{anchor.BoneName} ({anchor.Kind})"))}; {effectScene.Surfaces.Count} visual layers");
        if (asset.Mesh is { } mesh)
            text.AppendLine($"Rendered mesh: {mesh.Vertices.Length:N0} vertices, {mesh.Indices.Length / 3:N0} triangles, {mesh.Surfaces.Count} surfaces");
        else
            text.AppendLine("Rendered mesh: none");
        if (diagnostics.WholeModelBounds is { } wholeBounds)
            text.AppendLine($"Whole-model bounds: min {FormatVector(wholeBounds.Min)}, max {FormatVector(wholeBounds.Max)}, center {FormatVector(wholeBounds.Center)}");
        if (diagnostics.SkeletonBounds is { } skeletonBounds)
            text.AppendLine($"Whole-rig bounds: min {FormatVector(skeletonBounds.Min)}, max {FormatVector(skeletonBounds.Max)}, center {FormatVector(skeletonBounds.Center)}");

        foreach (var slice in diagnostics.Slices)
        {
            text.AppendLine();
            text.AppendLine($"SLICE {slice.Index}  parts={slice.Parts.Count}, texture groups={slice.TexturePolygonGroupCount}, texture polygons={slice.TexturePolygonCount}, bone ties={slice.BoneTieCount}");
            text.AppendLine($"  Textures: {(slice.TextureNames.Count == 0 ? "(none)" : string.Join(", ", slice.TextureNames))}");
            foreach (var part in slice.Parts)
            {
                text.AppendLine(
                    $"  Part {part.Index}: vertices={part.VertexCount}, polygons={part.PolygonCount}, UVs={part.TextureCoordinateCount}, weighted vertices={part.WeightedVertexCount}, weights={part.WeightCount}");
            }

            if (slice.Bones.Count == 0)
            {
                text.AppendLine("  Bones: (none)");
                continue;
            }

            text.AppendLine($"  Bones ({slice.Bones.Count}):");
            foreach (var bone in slice.Bones)
            {
                var parent = bone.ParentIndex == bone.Index
                    ? "root"
                    : $"{bone.ParentIndex} {slice.Bones[bone.ParentIndex].Name}";
                text.AppendLine($"    [{bone.Index}] {bone.Name}  parent={parent}");
            }
        }

        ModelDataText.Text = text.ToString().TrimEnd();
    }

    private static string FormatVector(Vector3 value) =>
        $"({value.X:0.##}, {value.Y:0.##}, {value.Z:0.##})";

    private static string FormatRange(SacredDamageRange range) =>
        range.IsPresent ? $"{range.Minimum}-{range.Maximum}" : "0";

    private static string[] GetBoneNames(GrnModelDiagnostics? diagnostics) =>
        diagnostics?.Slices
            .SelectMany(static slice => slice.Bones)
            .Select(static bone => bone.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

    private async Task LoadSelectedModelTexturesAsync(
        GrnAsset asset,
        SacredItemDataModel selectedItem,
        EquipmentEffectScene effectScene,
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
        var loaded = new Dictionary<string, ModelTextureBinding>(StringComparer.OrdinalIgnoreCase);
        var failedCount = 0;
        var modelHasEffectTextureSurface = ModelHasEffectTextureSurface(asset, selectedItem, archive);
        var preferItemTexture = textureNames.Length == 1;

        if (textureNames.Length == 0)
        {
            if (selectedItem.TextureId != 0)
            {
                try
                {
                    var itemTexture = await archive.LoadTextureAsync(selectedItem.TextureId, cancellationToken);
                    loaded[itemTexture.Name] = new ModelTextureBinding(itemTexture);
                }
                catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
                {
                    failedCount++;
                }
            }

            failedCount += await LoadEquipmentEffectTexturesAsync(effectScene, archive, loaded, cancellationToken);
            if (loaded.Count == 0)
            {
                _modelViewer.ShowTextureStatus("no textures referenced");
                return;
            }

            await _modelViewer.ShowTexturesAsync(loaded, failedCount, cancellationToken);
            return;
        }

        _modelViewer.ShowTextureStatus($"loading {textureNames.Length} textures...");
        foreach (var textureName in textureNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reference = ModelTextureResolver.Resolve(
                archive,
                selectedItem.TextureId,
                selectedItem.EffectTextureId,
                selectedItem.GraphicFlags,
                modelHasEffectTextureSurface,
                preferItemTexture,
                textureName);

            try
            {
                if (string.IsNullOrWhiteSpace(reference.TextureName))
                    continue;

                var baseTexture = await archive.LoadTextureAsync(reference.TextureName, cancellationToken);
                baseTexture = baseTexture with { Animation = reference.Animation };

                TextureAsset? overlayTexture = null;
                if (!string.IsNullOrWhiteSpace(reference.OverlayTextureName))
                {
                    overlayTexture = await archive.LoadTextureAsync(reference.OverlayTextureName, cancellationToken);
                    overlayTexture = overlayTexture with { Animation = reference.OverlayAnimation };
                }

                loaded[textureName] = new ModelTextureBinding(baseTexture, overlayTexture, reference.OverlayMode);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
            {
                failedCount++;
            }
        }

        failedCount += await LoadEquipmentEffectTexturesAsync(effectScene, archive, loaded, cancellationToken);

        var result = new TextureLoadResult(loaded, failedCount);

        if (cancellationToken.IsCancellationRequested)
            return;

        await _modelViewer.ShowTexturesAsync(result.Textures, result.FailedCount, cancellationToken);
    }

    private static async Task<int> LoadEquipmentEffectTexturesAsync(
        EquipmentEffectScene effectScene,
        TexturePakArchive archive,
        IDictionary<string, ModelTextureBinding> loaded,
        CancellationToken cancellationToken)
    {
        var failedCount = 0;
        foreach (var textureName in effectScene.TextureNames)
        {
            if (loaded.ContainsKey(textureName))
                continue;

            try
            {
                var texture = await archive.LoadTextureAsync(textureName, cancellationToken);
                loaded[textureName] = new ModelTextureBinding(texture);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
            {
                failedCount++;
            }
        }

        return failedCount;
    }

    private static bool ModelHasEffectTextureSurface(
        GrnAsset asset,
        SacredItemDataModel selectedItem,
        TexturePakArchive archive)
    {
        if (asset.Mesh is null ||
            selectedItem.EffectTextureId == 0 ||
            !archive.TryGetTextureName(selectedItem.EffectTextureId, out var effectTextureName))
        {
            return false;
        }

        foreach (var surface in asset.Mesh.Surfaces)
        {
            if (!string.IsNullOrWhiteSpace(surface.TextureName) &&
                archive.TryResolveTextureName(surface.TextureName, out var resolvedName) &&
                string.Equals(resolvedName, effectTextureName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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

    private sealed record TextureLoadResult(IReadOnlyDictionary<string, ModelTextureBinding> Textures, int FailedCount);

    private ItemPreviewRotationMode SelectedRotationMode =>
        PreviewRotationModeComboBox.SelectedItem is ItemPreviewRotationMode mode
            ? mode
            : ItemPreviewRotationMode.LegacyCurrent;

    private static ItemPreviewRotationMode ResolveRotationMode(
        SacredItemDataModel item,
        ItemPreviewRotationMode requestedMode)
    {
        if (requestedMode != ItemPreviewRotationMode.Auto)
            return requestedMode;

        return item.EquipmentType is SacredEquipmentType.Bow or SacredEquipmentType.Crossbow or SacredEquipmentType.Shield
            ? ItemPreviewRotationMode.LegacyCurrent
            : ItemPreviewRotationMode.DirectYawPitchRoll;
    }

    private ItemPreviewPivotMode SelectedPivotMode =>
        PreviewPivotModeComboBox.SelectedItem is ItemPreviewPivotMode mode
            ? mode
            : ItemPreviewPivotMode.BoundsCenter;

    private void ExperimentModeComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingBoneSelector)
            return;

        _selectedPivotBoneName = PreviewPivotBoneComboBox.SelectedItem as string;
        if (!_hasLoaded || DataGrid.SelectedItem is not SacredItemDataModel selectedItem)
            return;

        if (string.IsNullOrWhiteSpace(selectedItem.ModelName))
            return;

        SetConfirmPreviewState(null, false, GetPreviewConfirmationStatus(selectedItem, "Loading preview..."));
        var rotationMode = SelectedRotationMode;
        var pivotMode = SelectedPivotMode;
        _ = Task.Run(async () => await LoadModel(selectedItem, rotationMode, pivotMode));
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

        var rotationMode = ResolveRotationMode(item, SelectedRotationMode);
        var pivotMode = SelectedPivotMode;
        var viewerPreviewRotation = item.PreviewRotation;
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
            _selectedPivotBoneName,
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

    private IReadOnlyDictionary<uint, SacredItemPreviewConfirmationSummary> CreateConfirmedPreviewItems()
    {
        return _previewConfirmationsByItemId.ToDictionary(
            static pair => pair.Key,
            static pair => new SacredItemPreviewConfirmationSummary(
                pair.Value.ConfirmedAt,
                IsZeroRotation(pair.Value.UserRotationYawPitchRoll)));
    }

    private string GetPreviewConfirmationStatus(SacredItemDataModel item, string fallback)
    {
        return _previewConfirmationsByItemId.TryGetValue(item.ItemId, out var confirmation)
            ? $"Previously confirmed {confirmation.ConfirmedAt:yyyy-MM-dd HH:mm}; {confirmation.RotationMode}/{FormatPivot(confirmation)}; saved user rot {FormatRotationDegrees(confirmation.UserRotationYawPitchRollDegrees)}. Confirm again to update."
            : fallback;
    }

    private static string FormatPivot(SacredItemPreviewConfirmation confirmation) =>
        confirmation.PivotMode == ItemPreviewPivotMode.SelectedBone &&
        !string.IsNullOrWhiteSpace(confirmation.PivotBoneName)
            ? $"{confirmation.PivotMode} ({confirmation.PivotBoneName})"
            : confirmation.PivotMode.ToString();

    private static Vector3 ToVector3(RotationVectorData rotation)
    {
        return new Vector3(rotation.X, rotation.Y, rotation.Z);
    }

    private static bool IsZeroRotation(RotationVectorData rotation)
    {
        return rotation is { X: 0.0f, Y: 0.0f, Z: 0.0f };
    }

    private static string FormatRotationDegrees(RotationVectorData rotation)
    {
        return $"yaw {rotation.X:0.#}, pitch {rotation.Y:0.#}, roll {rotation.Z:0.#} deg";
    }

}
