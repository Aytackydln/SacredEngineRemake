using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Sacred.Assets.Equipment;
using Sacred.GameplayHost.Equipment;

namespace AssetViewer.AssetViewer;

/// <summary>Inspect existing special-item records without claiming a random special drop.</summary>
internal sealed class ItemDropAuthoredNamePane : StackPanel
{
    private readonly AssetViewerSession _session;
    private sealed record Choice(uint Type, string Name, bool Set)
    {
        public override string ToString() => $"{Name} · {(Set ? "Set" : "Unique")} · {Type}";
    }
    private readonly ComboBox _items = new() { MinWidth = 480, MaxDropDownHeight = 400 };
    private readonly Button _show = new() { Content = "Inspect original item", IsEnabled = false };
    private readonly Button _toggle = new() { Content = "Show unique / set item names" };
    private readonly StackPanel _content = new() { Spacing = 6, IsVisible = false };

    public ItemDropAuthoredNamePane(AssetViewerSession session, Func<uint, Task> inspect)
    {
        _session = session;
        Spacing = 6;
        _content.Children.Add(new TextBlock { Text = "Select a unique or set piece to view its original name and attributes." });
        _content.Children.Add(_items); _content.Children.Add(_show);
        Children.Add(_toggle); Children.Add(_content);
        _toggle.Click += (_, _) => Expand(!_content.IsVisible);
        _show.Click += async (_, _) => { if (_items.SelectedItem is Choice item) await inspect(item.Type); };
    }

    public void Load(SacredEquipmentGenerationData data)
    {
        var names = new WorldEquipmentNames(data);
        var setItems = _session.Data.ItemSets.Where(set => !set.IsEmpty).SelectMany(set => set.ItemIds).ToHashSet();
        _items.ItemsSource = data.Templates.Equipment.Where(pair => setItems.Contains(pair.Key) || pair.Value.SetType != 0 || (pair.Value.RawFlags & 0xF0000) == 0xF0000)
            .Select(pair => new Choice(pair.Key, names.Existing(pair.Key).Text, setItems.Contains(pair.Key) || pair.Value.SetType != 0))
            .OrderBy(item => item.Name, StringComparer.CurrentCulture).ToArray();
        _items.SelectedIndex = 0; _show.IsEnabled = true;
    }

    public void Select(uint type)
    {
        _items.SelectedItem = _items.Items.Cast<Choice>().Single(item => item.Type == type);
        Expand(true);
    }

    private void Expand(bool visible) { _content.IsVisible = visible; _toggle.Content = visible ? "Hide unique / set item names" : "Show unique / set item names"; }
}
