using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SacredItemSimulator.Avalonia.ItemViewer;

public sealed partial class EnumFilterViewModel : ObservableObject
{
    private readonly Action<EnumFilterViewModel> _selectionChanged;
    private bool _suppressSelectionChanged;

    public EnumFilterViewModel(
        string propertyName,
        bool isFlags,
        IEnumerable<EnumFilterOption> options,
        IReadOnlySet<ulong>? selectedValues,
        Action<EnumFilterViewModel> selectionChanged)
    {
        PropertyName = propertyName;
        IsFlags = isFlags;
        _selectionChanged = selectionChanged;

        foreach (var option in options)
        {
            Options.Add(new EnumFilterOptionViewModel(
                option.NumericValue,
                option.DisplayText,
                selectedValues?.Contains(option.NumericValue) == true,
                OnOptionSelectionChanged));
        }
    }

    public string PropertyName { get; }

    public bool IsFlags { get; }

    public ObservableCollection<EnumFilterOptionViewModel> Options { get; } = [];

    public IEnumerable<ulong> SelectedNumericValues => Options
        .Where(static option => option.IsSelected)
        .Select(static option => option.NumericValue);

    public int SelectedCount => Options.Count(static option => option.IsSelected);

    public string Summary
    {
        get
        {
            var selectedOptions = Options
                .Where(static option => option.IsSelected)
                .ToArray();

            return selectedOptions.Length switch
            {
                0 => "All",
                1 => selectedOptions[0].DisplayText,
                _ => $"{selectedOptions.Length} selected"
            };
        }
    }

    [RelayCommand]
    private void Clear()
    {
        if (SelectedCount == 0)
            return;

        _suppressSelectionChanged = true;
        foreach (var option in Options)
            option.IsSelected = false;
        _suppressSelectionChanged = false;

        OnOptionSelectionChanged();
    }

    private void OnOptionSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(Summary));

        if (!_suppressSelectionChanged)
            _selectionChanged(this);
    }
}

public sealed class EnumFilterOptionViewModel : ObservableObject
{
    private readonly Action _selectionChanged;
    private bool _isSelected;

    public EnumFilterOptionViewModel(
        ulong numericValue,
        string displayText,
        bool isSelected,
        Action selectionChanged)
    {
        NumericValue = numericValue;
        DisplayText = displayText;
        _isSelected = isSelected;
        _selectionChanged = selectionChanged;
    }

    public ulong NumericValue { get; }

    public string DisplayText { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
                _selectionChanged();
        }
    }
}

public readonly record struct EnumFilterOption(ulong NumericValue, string DisplayText);
