using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sacred.Core.Pak.Weapon;

namespace Sacred.ItemViewer.Avalonia.ItemViewer;

public enum PreviewConfirmationFilterMode
{
    All,
    Confirmed,
    Unconfirmed
}

public partial class SacredItemDataTableViewModel : ObservableObject
{
    private readonly List<SacredItemDataModel> _allEquipments;
    private readonly List<SacredItemDataModel> _filteredEquipments;
    private readonly Dictionary<string, ActiveEnumFilter> _enumFilters = [];

    public SacredItemDataTableViewModel(
        List<SacredEquipment> allEquipments,
        FrozenDictionary<string, string> translationMap)
    {
        _allEquipments = allEquipments
            .Select(equipment => SacredItemDataModel.FromSacredEquipment(equipment, translationMap))
            .ToList();
        _filteredEquipments = new List<SacredItemDataModel>(_allEquipments);
    }

    public ObservableCollection<SacredItemDataModel> CurrentPageItems { get; } = [];

    public ObservableCollection<EnumFilterViewModel> EnumFilters { get; } = [];

    public IReadOnlyList<PreviewConfirmationFilterMode> PreviewConfirmationFilterOptions { get; } =
        Enum.GetValues<PreviewConfirmationFilterMode>();

    public int TotalItems => _filteredEquipments.Count;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    public partial int PageSize { get; private set; } = 30;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNextPage))]
    [NotifyPropertyChangedFor(nameof(HasPreviousPage))]
    public partial int CurrentPage { get; private set; } = -1;

    public int TotalPages => Math.Max(1, (_filteredEquipments.Count + PageSize - 1) / PageSize);

    public bool HasNextPage => CurrentPage < TotalPages - 1;
    public bool HasPreviousPage => CurrentPage > 0;

    [ObservableProperty]
    public partial IComparer<SacredItemDataModel> Comparer { get; set; } =
        Comparer<SacredItemDataModel>.Create((x, y) => 0);

    [ObservableProperty]
    public partial bool FilterHasModel { get; set; }

    [ObservableProperty]
    public partial PreviewConfirmationFilterMode PreviewConfirmationFilter { get; set; }

    public event Action<bool>? FilterHasModelChanged;

    partial void OnComparerChanged(IComparer<SacredItemDataModel> value) => ApplySort();

    partial void OnFilterHasModelChanged(bool value)
    {
        RebuildFilteredEquipments();
        NotifyPagingStateChanged();
        LoadPage(0);
        FilterHasModelChanged?.Invoke(value);
    }

    partial void OnPreviewConfirmationFilterChanged(PreviewConfirmationFilterMode value)
    {
        RebuildFilteredEquipments();
        NotifyPagingStateChanged();
        LoadPage(0);
    }

    public void SetConfirmedPreviewItems(IReadOnlyDictionary<uint, SacredItemPreviewConfirmationSummary> confirmedItems)
    {
        for (var i = 0; i < _allEquipments.Count; i++)
        {
            var item = _allEquipments[i];
            _allEquipments[i] = confirmedItems.TryGetValue(item.ItemId, out var confirmation)
                ? item with
                {
                    PreviewConfirmed = true,
                    PreviewConfirmedAt = confirmation.ConfirmedAt,
                    PreviewConfirmedUserRotationIsZero = confirmation.UserRotationIsZero
                }
                : item with
                {
                    PreviewConfirmed = false,
                    PreviewConfirmedAt = null,
                    PreviewConfirmedUserRotationIsZero = false
                };
        }

        RebuildFilteredEquipments();
        NotifyPagingStateChanged();
        LoadPage(CurrentPage);
    }

    public void SetEnumFilters(IEnumerable<EnumFilterViewModel> filters)
    {
        EnumFilters.Clear();
        foreach (var filter in filters)
            EnumFilters.Add(filter);
    }

    public void SetEnumFilter(
        string propertyName,
        bool isFlags,
        IEnumerable<ulong> selectedValues)
    {
        var selectedNumericValues = selectedValues
            .ToHashSet();

        if (selectedNumericValues.Count == 0)
        {
            _enumFilters.Remove(propertyName);
        }
        else
        {
            var property = typeof(SacredItemDataModel).GetProperty(propertyName)
                           ?? throw new ArgumentException($"Unknown item property: {propertyName}", nameof(propertyName));

            _enumFilters[propertyName] = new ActiveEnumFilter(
                isFlags,
                selectedNumericValues,
                property);
        }

        RebuildFilteredEquipments();
        NotifyPagingStateChanged();
        LoadPage(0);
    }

    public void LoadPage(int page)
    {
        if (page < 0)
            page = 0;
        if (page >= TotalPages)
            page = TotalPages - 1;

        CurrentPage = page;

        CurrentPageItems.Clear();

        var query = _filteredEquipments
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

    private void ApplySort()
    {
        LoadPage(CurrentPage);
    }

    private void RebuildFilteredEquipments()
    {
        _filteredEquipments.Clear();
        _filteredEquipments.AddRange(_allEquipments.Where(PassesFilters));
    }

    private bool PassesFilters(SacredItemDataModel item)
    {
        foreach (var filter in _enumFilters.Values)
        {
            var value = filter.Property.GetValue(item);
            if (value is null)
                return false;

            var numericValue = Convert.ToUInt64(value);
            if (filter.IsFlags)
            {
                if (!PassesFlagsFilter(numericValue, filter.SelectedValues))
                    return false;
            }
            else if (!filter.SelectedValues.Contains(numericValue))
            {
                return false;
            }
        }

        if (FilterHasModel)
        {
            if (string.IsNullOrEmpty(item.ModelName))
            {
                return false;
            }
        }

        if (PreviewConfirmationFilter == PreviewConfirmationFilterMode.Confirmed && !item.PreviewConfirmed)
            return false;

        if (PreviewConfirmationFilter == PreviewConfirmationFilterMode.Unconfirmed
            && (string.IsNullOrEmpty(item.ModelName) || item.PreviewConfirmed))
            return false;

        return true;
    }

    private static bool PassesFlagsFilter(ulong numericValue, HashSet<ulong> selectedValues)
    {
        if (numericValue == 0)
            return selectedValues.Contains(0);

        return selectedValues
            .Where(static selectedValue => selectedValue != 0)
            .Any(selectedValue => (numericValue & selectedValue) != 0);
    }

    private void NotifyPagingStateChanged()
    {
        OnPropertyChanged(nameof(TotalItems));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(HasNextPage));
        OnPropertyChanged(nameof(HasPreviousPage));
    }

    private sealed record ActiveEnumFilter(
        bool IsFlags,
        HashSet<ulong> SelectedValues,
        PropertyInfo Property);
}

public readonly record struct SacredItemPreviewConfirmationSummary(
    DateTimeOffset ConfirmedAt,
    bool UserRotationIsZero);
