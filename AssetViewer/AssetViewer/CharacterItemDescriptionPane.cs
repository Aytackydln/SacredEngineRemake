using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Pak.Weapon.Descriptions;
using Sacred.Core.Pak.Weapon.Details;
using Sacred.Inventory.Items;
using Sacred.UI.Inventory;

namespace AssetViewer.AssetViewer;

internal sealed class CharacterItemDescriptionPane : UserControl
{
    private readonly SacredEquipmentDetailsBuilder _details;
    private readonly SacredEquipmentDescriptionFormatter _formatter;
    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _heading = new() { FontWeight = FontWeight.Bold };
    private readonly ModelPreviewPane _preview;
    private uint? _previewedItemId;

    public CharacterItemDescriptionPane(AssetViewerSession session)
    {
        _preview = new(session, compact: true);
        _details = new(session.Data.ItemSets);
        _formatter = new(session.Data.GameResStore);
        var root = new DockPanel { Margin = new Thickness(0, 8, 0, 8) };
        DockPanel.SetDock(_heading, Dock.Top); root.Children.Add(_heading);
        var item = new Grid { ColumnDefinitions = new("2*,3*"), ColumnSpacing = 12, Margin = new Thickness(0, 8, 0, 0) };
        item.Children.Add(_preview);
        var description = new ScrollViewer { Content = _text };
        Grid.SetColumn(description, 1); item.Children.Add(description);
        root.Children.Add(item); Content = root;
        Clear();
    }

    public void Show(SacredEquipment definition)
    {
        _heading.Text = "Selected item";
        _text.Text = SacredEquipmentDescriptionText.Format(_formatter.Format(_details.Create(definition)));
        ShowModel(definition);
    }

    public void Show(SacredItemInstance instance)
    {
        var details = _details.Create(instance.Definition);
        var socketCount = 0;
        var slots = instance.SlotTypes;
        foreach (var slot in slots) if (slot != 0) socketCount++;
        details = details with
        {
            Identity = details.Identity with { Level = (byte)instance.Level, Price = instance.Price, SocketCount = socketCount },
            BaseValues = new(instance.Damage, instance.BaseStats, instance.Bonuses.Where(bonus => bonus.Code == 812).Sum(bonus => bonus.Value)),
            Requirements = instance.Requirements,
            Bonuses = instance.Bonuses
        };
        _heading.Text = "Equipped item";
        _text.Text = SacredEquipmentDescriptionText.Format(_formatter.Format(details));
        ShowModel(instance.Definition);
    }

    public void Clear()
    {
        _heading.Text = "Item description";
        _text.Text = "Select an equipped item or an item from the list.";
        _previewedItemId = null;
        _preview.ShowStatus("Select an item to preview.");
    }

    public Task Ready => _preview.CurrentLoad;
    public void Cancel() => _preview.Cancel();
    public void SaveScreenshot(string path) => _preview.SaveScreenshot(path);

    private void ShowModel(SacredEquipment definition)
    {
        if (_previewedItemId == definition.IdemId) return;
        _previewedItemId = definition.IdemId;
        if (string.IsNullOrWhiteSpace(definition.Item.ModelName))
        {
            _preview.ShowStatus("This item has no 3D model.");
            return;
        }
        Console.WriteLine($"[Assets] Item preview selected: {definition.IdemId}; {definition.Item.ModelName}.");
        _ = _preview.LoadInventoryItemAsync(definition);
    }

    public void PrintDescription() => Console.WriteLine($"[Item description] {_heading.Text}\n{_text.Text}");
}
