using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
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
    private const string GameDir = @"E:\SteamLibrary\steamapps\common\Sacred Gold";

    private const string GermanRes = GameDir + @"\scripts\de\SRglbl.res";

    private const string GlobalRes = GameDir + @"\scripts\us\global.res";
    private const string SrGlobalRes = GameDir + @"\scripts\us\SRglbl.res";

    private const string WeaponPak = GameDir + @"\pak\Weapon.pak";
    private const string ItemsPak = GameDir + @"\pak\Items.pak";
    private const string TexturePak = GameDir + @"\pak\texture.pak";

    private static readonly SacredGameDirectories GameDirectories = new()
    {
        GlobalResourcesPath = GlobalRes,
        LocalResourcesPath = SrGlobalRes,
        ReferenceResourcesPath = GermanRes,
        WeaponsPakPath = WeaponPak,
        ItemsPakPath = ItemsPak,
        TexturesPakPath = TexturePak,
    };

    private SacredItemDataTableViewModel _tableViewModel = new([], FrozenDictionary<string, string>.Empty);
    private readonly ModelViewerControl _modelViewer = new();
    private ModelsPakArchive? _modelsPakArchive;
    private TexturePakArchive? _texturePakArchive;
    private CancellationTokenSource? _modelLoadCancellation;

    public SacredItemDataTable()
    {
        InitializeComponent();
        ModelViewerPanel.Children.Add(_modelViewer);
    }

    private void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        var sacredGameData = SacredGameData.LoadFromGamePaks(GameDirectories);
        var items = sacredGameData.GamePakStore.Weapons.Values.ToList();

        _tableViewModel = new SacredItemDataTableViewModel(items, sacredGameData.GameResStore.TranslatedStrings);
        DataContext = _tableViewModel;
        var pakDirectory = Path.Combine(GameDir, "pak");
        _modelsPakArchive = ModelsPakArchive.Load(Path.Combine(pakDirectory, "models.pak"));
        _texturePakArchive = TexturePakArchive.LoadFromDirectory(pakDirectory);

        _tableViewModel.LoadPage(0);
    }

    private async void DataGrid_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_modelsPakArchive is null)
        {
            _modelViewer.ShowStatus("Model archive is not loaded.");
            return;
        }

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

        _modelLoadCancellation?.Cancel();
        _modelLoadCancellation?.Dispose();
        _modelLoadCancellation = new CancellationTokenSource();
        var cancellationToken = _modelLoadCancellation.Token;
        _modelViewer.ShowStatus($"{selectedItem.ModelName}: loading...");

        try
        {
            var archive = _modelsPakArchive;
            var modelName = selectedItem.ModelName;
            var asset = await archive.LoadModelAsync(modelName, GrnMeshExtractionMode.PrimarySlice, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
                return;

            _modelViewer.ShowModel(asset, selectedItem.PreviewRotation);
            await LoadSelectedModelTexturesAsync(asset, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
        {
            _modelViewer.ShowStatus($"{selectedItem.ModelName}: {ex.Message}");
        }
    }

    private async Task LoadSelectedModelTexturesAsync(GrnAsset asset, CancellationToken cancellationToken)
    {
        if (_texturePakArchive is null || asset.Mesh is null)
            return;

        var textureNames = asset.Mesh.Surfaces
            .Select(static surface => surface.TextureName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name!)
            .ToArray();
        if (textureNames.Length == 0)
        {
            _modelViewer.ShowTextureStatus("no textures referenced");
            return;
        }

        _modelViewer.ShowTextureStatus($"loading {textureNames.Length} textures...");
        var archive = _texturePakArchive;
        var loaded = new Dictionary<string, TextureAsset>(StringComparer.OrdinalIgnoreCase);
        var failedCount = 0;
        foreach (var textureName in textureNames)
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

    private sealed record TextureLoadResult(
        IReadOnlyDictionary<string, TextureAsset> Textures,
        int FailedCount);
}
