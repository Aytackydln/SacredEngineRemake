using System;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AssetViewer.AssetViewer;

/// <summary>Stable rank editor; refreshes preserve keyboard focus.</summary>
internal sealed class CharacterRankRow : UserControl
{
    private readonly NumericUpDown _base = new() { Minimum = 1, Maximum = byte.MaxValue, Value = 1, ShowButtonSpinner = false, FormatString = "0" };
    private readonly TextBlock _bonus = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _total = new() { VerticalAlignment = VerticalAlignment.Center };
    private bool _updating;
    private readonly Border _name = new();

    public Control NameContent { set => _name.Child = value; }

    public CharacterRankRow(string name, Action<int> setRank)
    {
        var grid = new Grid { ColumnDefinitions = new("*,70,50,50,30"), ColumnSpacing = 4 };
        _name.Child = new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(_name);
        Grid.SetColumn(_base, 1); grid.Children.Add(_base);
        Grid.SetColumn(_bonus, 2); grid.Children.Add(_bonus);
        Grid.SetColumn(_total, 3); grid.Children.Add(_total);
        var remove = new Button { Content = "×", Padding = new(4), HorizontalAlignment = HorizontalAlignment.Stretch };
        ToolTip.SetTip(remove, "Remove acquired skill or combat art");
        Grid.SetColumn(remove, 4); grid.Children.Add(remove);
        Content = grid;
        _base.ValueChanged += (_, _) => { if (!_updating) setRank((int)(_base.Value ?? 1)); };
        remove.Click += (_, _) => setRank(0);
    }

    public void Update(int baseLevel, int bonusLevel, int total)
    {
        _updating = true;
        _base.Value = baseLevel;
        _bonus.Text = bonusLevel.ToString();
        _total.Text = total.ToString();
        _updating = false;
    }
}
