using AssetViewer.Browser.Rendering;

namespace AssetViewer.ItemViewer;

internal static class ModelViewportFactory
{
    internal static IModelViewport Create() => new WebGpuModelViewportHost();
}
