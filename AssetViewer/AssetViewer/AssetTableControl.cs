using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace AssetViewer.AssetViewer;

/// <summary>A bounded table page with text search; raw archive catalogs can contain thousands of rows.</summary>
internal sealed class AssetTableControl<T> : UserControl where T : class
{
    private const int PageSize = 100;
    private readonly T[] _rows;
    private readonly Func<T, string> _searchText;
    private readonly TextBox _search = new() { PlaceholderText = "Search name or ID", Width = 260 };
    private readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _previous = new() { Content = "Previous" };
    private readonly Button _next = new() { Content = "Next" };
    private T[] _filtered = [];
    private int _page;
    private T? _pendingSelection;
    private Func<T, bool>? _rowFilter;

    public AssetTableControl(IEnumerable<T> rows, Func<T, string> searchText)
    {
        _rows = rows.ToArray();
        _searchText = searchText;
        Table = new DataGrid { IsReadOnly = true, AutoGenerateColumns = true, SelectionMode = DataGridSelectionMode.Single };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        bar.Children.Add(_search);
        bar.Children.Add(_previous);
        bar.Children.Add(_next);
        bar.Children.Add(_count);
        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(Table);
        Content = root;
        Table.SelectionChanged += (_, args) =>
        {
            if (args.AddedItems.OfType<T>().LastOrDefault() is { } row) SetSelected(row);
        };
        _search.TextChanged += (_, _) => Filter();
        _previous.Click += (_, _) => { _page--; ShowPage(); };
        _next.Click += (_, _) => { _page++; ShowPage(); };
        Filter();
    }

    public DataGrid Table { get; }
    public T? Selected { get; private set; }
    public event Action<T>? SelectedChanged;
    public int FilteredCount => _filtered.Length;
    public void SetSearchText(string text)
    {
        _search.Text = text;
        Filter();
    }

    public void SetRowFilter(Func<T, bool>? predicate)
    {
        _rowFilter = predicate;
        Filter();
        if (Selected is { } selected && !(_rowFilter?.Invoke(selected) ?? true) && _filtered.FirstOrDefault() is { } first)
        {
            SetSelected(first);
            ShowSelectionAfterLayout(first);
        }
    }

    private void SetSelected(T row)
    {
        if (ReferenceEquals(Selected, row)) return;
        Selected = row;
        SelectedChanged?.Invoke(row);
    }

    public void Select(Func<T, bool> predicate)
    {
        var row = _rows.FirstOrDefault(row => predicate(row) && (_rowFilter?.Invoke(row) ?? true))
            ?? throw new ArgumentException("No matching asset in the active filter.");
        if (!string.IsNullOrEmpty(_search.Text))
        {
            _pendingSelection = row;
            _search.Text = string.Empty;
        }
        _filtered = _rows.Where(row => _rowFilter?.Invoke(row) ?? true).ToArray();
        _page = Array.IndexOf(_filtered, row) / PageSize;
        ShowPage();
        SetSelected(row);
        ShowSelectionAfterLayout(row);
    }

    private void Filter()
    {
        var query = _search.Text?.Trim() ?? string.Empty;
        _filtered = _rows.Where(row => (_rowFilter?.Invoke(row) ?? true) &&
            _searchText(row).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        _page = 0;
        if (_pendingSelection is { } pending && Array.IndexOf(_filtered, pending) is var index && index >= 0)
        {
            _pendingSelection = null;
            _page = index / PageSize;
            ShowPage();
            ShowSelectionAfterLayout(pending);
            return;
        }
        ShowPage();
    }

    private void ShowSelectionAfterLayout(T row)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(Selected, row)) return;
            Table.SelectedItem = row;
            Table.ScrollIntoView(row, null);
        }, DispatcherPriority.Background);
    }

    private void ShowPage()
    {
        var pages = Math.Max(1, (_filtered.Length + PageSize - 1) / PageSize);
        _page = Math.Clamp(_page, 0, pages - 1);
        Table.ItemsSource = _filtered.Skip(_page * PageSize).Take(PageSize).ToArray();
        _count.Text = $"{_filtered.Length:N0} entries · page {_page + 1}/{pages}";
        _previous.IsEnabled = _page > 0;
        _next.IsEnabled = _page < pages - 1;
    }
}
