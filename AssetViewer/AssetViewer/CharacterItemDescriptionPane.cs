using System.Threading.Tasks;
using Avalonia.Controls;
using Sacred.Core.Pak.Weapon;
using Sacred.Inventory.Items;

namespace AssetViewer.AssetViewer;

/// <summary>Character and backpack selection use the same native description as the drop simulator.</summary>
internal sealed class CharacterItemDescriptionPane : UserControl
{
    private readonly ItemDropDescriptionPane _pane;

    public CharacterItemDescriptionPane(AssetViewerSession session)
    {
        _pane = new(session);
        Content = _pane;
        Clear();
    }

    public void Show(SacredEquipment definition) => _pane.Show(SacredItemInstance.FromDefinition(definition));
    public void Show(SacredItemInstance instance) => _pane.Show(instance);
    public void Show(SacredItemInstance instance, string heading) => _pane.Show(instance);
    public void Clear() => Clear("Item description", "Select an equipped item or an item from the list.", "Select an item to preview.");
    public void Clear(string heading, string description, string previewMessage) => _pane.Clear(description, previewMessage);
    public Task Ready => _pane.Ready;
    public void Cancel() => _pane.Cancel();
    public void SaveScreenshot(string path) => _pane.SaveScreenshot(path);
    public void PrintDescription() => _pane.PrintDescription();
}
