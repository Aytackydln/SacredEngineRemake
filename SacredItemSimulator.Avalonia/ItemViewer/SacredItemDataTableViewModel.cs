using System.Collections;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sacred.Core.Weapon;

namespace SacredItemSimulator.Avalonia.ItemViewer;

public partial class SacredItemDataTableViewModel(List<SacredEquipment> allEquipments, FrozenDictionary<string, string> translationMap) : ObservableObject
{
    public ObservableCollection<SacredItemDataModel> CurrentPageItems { get; } = [];
    
    public int TotalItems => _allEquipments.Count;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    public partial int PageSize { get; private set; } = 50;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNextPage))]
    [NotifyPropertyChangedFor(nameof(HasPreviousPage))]
    public partial int CurrentPage { get; private set; } = -1;
    
    public int TotalPages => (_allEquipments.Count + PageSize - 1) / PageSize;
    
    public bool HasNextPage => CurrentPage < TotalPages - 1;
    public bool HasPreviousPage => CurrentPage > 0;
    
    private readonly List<SacredItemDataModel> _allEquipments = allEquipments
        .Select(equipment => SacredItemDataModel.FromSacredEquipment(equipment, translationMap))
        .ToList();

    [ObservableProperty]
    public partial IComparer<SacredItemDataModel> Comparer { get; set; } = Comparer<SacredItemDataModel>.Create((x, y) => 0);

    partial void OnComparerChanged(IComparer<SacredItemDataModel> value) => ApplySort();

    private void ApplySort()
    {
        LoadPage(CurrentPage);
    }

    public void LoadPage(int page)
    {
        CurrentPage = page;

        CurrentPageItems.Clear();

        var query =  _allEquipments
            .OrderBy(x => x, Comparer)
            .Skip(page * PageSize)
            .Take(PageSize);
        foreach (var item in query)
        {
            CurrentPageItems.Add(item);
        }
    }

    [RelayCommand]
    private void NextPage()
    {
        LoadPage(CurrentPage + 1);
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (CurrentPage > 0)
        {
            LoadPage(CurrentPage - 1);
        }
    }
}