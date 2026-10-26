using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Sacred.Inventory.Actors;

namespace AssetViewer.AssetViewer;

/// <summary>The viewer's item browser and commands, separate from native equipment slot artwork.</summary>
internal sealed class CharacterBackpackPane : UserControl
{
    private readonly CharacterEquipmentRow[] _equipment;
    private readonly ListBox _matches = new() { MinHeight = 120 };
    private readonly TextBox _search = new() { PlaceholderText = "Search equippable item by name or ID" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _equip = new() { Content = "Equip", IsEnabled = false };
    private CharacterSlotRow? _slot;
    private SacredCharacterClass? _class;
    public CharacterEquipmentRow? Selected => _matches.SelectedItem as CharacterEquipmentRow;
    public event Action? SelectionChanged;
    public event Action<CharacterSlotRow, CharacterEquipmentRow>? EquipRequested;
    public event Action<CharacterSlotRow>? UnequipRequested;
    public event Action? ClearRequested;

    public CharacterBackpackPane(CharacterEquipmentRow[] equipment)
    {
        _equipment = equipment;
        var root = new DockPanel();
        var header = new StackPanel { Spacing = 6 };
        header.Children.Add(new TextBlock { Text = "Backpack / item definitions", FontWeight = FontWeight.Bold });
        header.Children.Add(_search);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var unequip = new Button { Content = "Unequip" };
        var clear = new Button { Content = "Clear all" };
        buttons.Children.Add(_equip); buttons.Children.Add(unequip); buttons.Children.Add(clear);
        header.Children.Add(buttons);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
        root.Children.Add(_matches); Content = root;
        _search.TextChanged += (_, _) => Filter();
        _matches.SelectionChanged += (_, _) => { _equip.IsEnabled = Selected is not null && _slot is not null; SelectionChanged?.Invoke(); };
        _equip.Click += (_, _) => { if (_slot is { } slot && Selected is { } item) EquipRequested?.Invoke(slot, item); };
        unequip.Click += (_, _) => { if (_slot is { } slot) UnequipRequested?.Invoke(slot); };
        clear.Click += (_, _) => ClearRequested?.Invoke();
    }

    public void Configure(CharacterSlotRow? slot, SacredCharacterClass? characterClass)
    {
        _slot = slot; _class = characterClass;
        _matches.SelectedItem = null;
        Filter();
    }

    public void Select(uint id) => _matches.SelectedItem = _matches.Items.OfType<CharacterEquipmentRow>().Single(item => item.EntryId == id);

    private void Filter()
    {
        var query = _search.Text?.Trim() ?? string.Empty;
        var matches = _equipment.Where(item => (_slot is { } slot
                ? CharacterEquipment.CanEquip(slot.Type, _class, item.Item)
                : _class is not { } characterClass || EquipmentSlotRules.CanEquip(characterClass, item.Item))
            && item.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        _matches.ItemsSource = matches;
        _equip.IsEnabled = false;
        _status.Text = $"{matches.Length:N0} matching definitions. Equipping creates an item with authored values.";
        Console.WriteLine($"[Assets] Equipment filter: slot={_slot?.Type.ToString() ?? "all"}; class={_class?.ToString() ?? "unrestricted"}; matches={matches.Length}.");
        SelectionChanged?.Invoke();
    }
}
