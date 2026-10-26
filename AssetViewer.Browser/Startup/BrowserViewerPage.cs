global using Window = AssetViewer.Browser.Startup.BrowserViewerPage;
using Avalonia.Controls;

namespace AssetViewer.Browser.Startup;

/// <summary>Hosts the existing viewer window content in a browser tab.</summary>
internal class BrowserViewerPage : UserControl
{
    public string Title { get; set; } = string.Empty;
    public event EventHandler? Closed;
    internal void Close() => Closed?.Invoke(this, EventArgs.Empty);
}
