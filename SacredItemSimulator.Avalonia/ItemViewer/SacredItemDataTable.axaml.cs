using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Sacred.Assets;
using Sacred.Core;
using Sacred.Granny;

namespace SacredItemSimulator.Avalonia.ItemViewer;

public partial class SacredItemDataTable : UserControl
{
    private const string DefaultGameDir = @"E:\SteamLibrary\steamapps\common\Sacred Gold";

    private SacredItemDataTableViewModel _tableViewModel = new([], FrozenDictionary<string, string>.Empty);
    private readonly ModelViewerControl _modelViewer = new();
    private readonly SacredItemFilterSaveStore _filterSaveStore = SacredItemFilterSaveStore.CreateDefault();
    private Dictionary<string, HashSet<ulong>> _savedEnumFilters = [];
    private string _gameDir = DefaultGameDir;
    private ModelsPakArchive _modelsPakArchive = null!;
    private TexturePakArchive _texturePakArchive = null!;
    private CancellationTokenSource? _modelLoadCancellation;
    private bool _hasLoaded;

    public SacredItemDataTable()
    {
        InitializeComponent();
        ModelViewerPanel.Children.Add(_modelViewer);
        ModelYawSlider.ValueChanged += (_, _) => UpdateModelRotationFromSliders();
        ModelPitchSlider.ValueChanged += (_, _) => UpdateModelRotationFromSliders();
        ModelRollSlider.ValueChanged += (_, _) => UpdateModelRotationFromSliders();
        UpdateModelRotationFromSliders();
    }

    private void ResetModelRotationSliders()
    {
        UpdateModelRotationFromSliders();
    }

    private void UpdateModelRotationFromSliders()
    {
        var yaw = ModelYawSlider.Value;
        var pitch = ModelPitchSlider.Value;
        var roll = ModelRollSlider.Value;

        ModelYawValueText.Text = FormatSliderDegrees(yaw);
        ModelPitchValueText.Text = FormatSliderDegrees(pitch);
        ModelRollValueText.Text = FormatSliderDegrees(roll);

        _modelViewer.SetUserRotation(
            DegreesToRadians(yaw),
            DegreesToRadians(pitch),
            DegreesToRadians(roll));
    }

    private static string FormatSliderDegrees(double degrees)
    {
        return $"{degrees:0.#} deg";
    }

    private static float DegreesToRadians(double degrees)
    {
        return (float)(degrees * Math.PI / 180.0);
    }

    private async void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_hasLoaded)
            return;

        _hasLoaded = true;
        DataGrid.IsVisible = false;

        var savedSettings = _filterSaveStore.Load();
        _savedEnumFilters = savedSettings.EnumFilters;
        _gameDir = await PromptForGameDirectoryAsync(savedSettings.GameDirectory);
        SaveSettings(savedSettings.FilterHasModel);

        var gameDirectories = CreateGameDirectories(_gameDir);
        var sacredGameData = SacredGameData.LoadFromGamePaks(gameDirectories);
        var items = sacredGameData.GamePakStore.Weapons.Values.ToList();

        _tableViewModel = new SacredItemDataTableViewModel(items, sacredGameData.GameResStore.TranslatedStrings);
        _tableViewModel.FilterHasModel = savedSettings.FilterHasModel;
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
        ResetModelRotationSliders();

        if (sender is not DataGrid { SelectedItem: SacredItemDataModel selectedItem })
        {
            _modelViewer.ShowStatus("Select an item to load its model.");
            return;
        }

        if (string.IsNullOrWhiteSpace(selectedItem.ModelName))
        {
            _modelViewer.ShowStatus($"{selectedItem.ItemName}: no model name.");
            return;
        }

        _ = Task.Run(async () => await LoadModel(selectedItem));
    }

    private async Task LoadModel(SacredItemDataModel selectedItem)
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

            if (cancellationToken.IsCancellationRequested)
                return;

            _modelViewer.ShowModel(asset, selectedItem.PreviewRotation);
            await _modelViewer.Dispatcher.InvokeAsync(UpdateModelRotationFromSliders);
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

}
