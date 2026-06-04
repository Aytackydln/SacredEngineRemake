using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Sacred.Core;

namespace SacredItemSimulator.Avalonia.ItemViewer;

public partial class SacredItemDataTable : UserControl
{
    private const string GameDir = @"E:\SteamLibrary\steamapps\common\Sacred Gold";

    private const string GermanRes = GameDir + @"\scripts\de\SRglbl.res";

    private const string GlobalRes = GameDir + @"\scripts\us\global.res";
    private const string SrGlobalRes = GameDir + @"\scripts\us\SRglbl.res";

    private const string WeaponPak = GameDir + @"\pak\Weapon.pak";
    private const string ItemsPak = GameDir + @"\pak\Items.pak";
    private const string TexturePak = GameDir + @"\pak\Texture.pak";

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

    public SacredItemDataTable()
    {
        InitializeComponent();
    }

    private void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        var sacredGameData = SacredGameData.LoadFromGamePaks(GameDirectories);
        var items = sacredGameData.GamePakStore.Weapons.Values.ToList();

        _tableViewModel = new SacredItemDataTableViewModel(items, sacredGameData.GameResStore.TranslatedStrings);
        DataContext = _tableViewModel;

        _tableViewModel.LoadPage(0);
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
}