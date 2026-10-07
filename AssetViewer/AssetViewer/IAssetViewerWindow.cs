using System.Threading.Tasks;

namespace AssetViewer.AssetViewer;

internal interface IAssetViewerWindow
{
    Task Ready { get; }
    void SaveScreenshot(string path);
}
