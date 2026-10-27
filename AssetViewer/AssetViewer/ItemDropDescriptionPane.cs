using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Sacred.Inventory.Items;

namespace AssetViewer.AssetViewer;

/// <summary>Drop simulator host; the inspection model is separate from the game's item description.</summary>
internal sealed class ItemDropDescriptionPane : UserControl
{
    private readonly ItemDescriptionControl _description;
    private readonly ModelPreviewPane _preview;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ScrollViewer _descriptionHost;
    private uint? _previewedItem;

    public ItemDropDescriptionPane(AssetViewerSession session)
    {
        _description = new(session);
        _preview = new(session, compact: true);
        _descriptionHost = new() { Content = _status };
        var body = new Grid { ColumnDefinitions = new("2*,3*"), ColumnSpacing = 12, Margin = new Thickness(0, 8) };
        body.Children.Add(_preview);
        Grid.SetColumn(_descriptionHost, 1); body.Children.Add(_descriptionHost);
        Content = body;
    }

    public Task Ready => Task.WhenAll(_description.Ready, _preview.CurrentLoad);

    public void Show(SacredItemInstance item)
    {
        _description.Show(item);
        _descriptionHost.Content = _description;
        if (_previewedItem == item.Definition.IdemId) return;
        _previewedItem = item.Definition.IdemId;
        if (string.IsNullOrWhiteSpace(item.Definition.Item.ModelName)) _preview.ShowStatus("This item has no 3D model.");
        else
        {
            Console.WriteLine($"[Assets] Item preview selected: {item.Definition.IdemId}; {item.Definition.Item.ModelName}.");
            _ = _preview.LoadInventoryItemAsync(item.Definition);
        }
    }

    public void Clear(string description, string previewMessage)
    {
        _description.Clear();
        _status.Text = description;
        _descriptionHost.Content = _status;
        _previewedItem = null;
        _preview.ShowStatus(previewMessage);
        Console.WriteLine($"[Drops] Description cleared: {previewMessage}");
    }

    public void PrintDescription() => _description.PrintDescription();
    public void SaveScreenshot(string path) => _preview.SaveScreenshot(path);
    public void Cancel() { _preview.Cancel(); _description.Dispose(); }
}
