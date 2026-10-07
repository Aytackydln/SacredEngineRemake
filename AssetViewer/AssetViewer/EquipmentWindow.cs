using System.Threading.Tasks;
using AssetViewer.ItemViewer;
using Avalonia.Controls;

namespace AssetViewer.AssetViewer;

internal sealed class EquipmentWindow : Window, IAssetViewerWindow
{
    private readonly SacredItemDataTable _table;

    public EquipmentWindow(AssetViewerSession session)
    {
        Title = "Sacred Asset Viewer · Equipment";
        Width = 1500; Height = 900;
        _table = new SacredItemDataTable(session);
        Content = _table;
    }

    public Task Ready => _table.PreviewReady;
    public void Select(uint id) => _table.SelectEquipment(id);
    public void SaveScreenshot(string path) => _table.SavePreviewScreenshot(path);
}
