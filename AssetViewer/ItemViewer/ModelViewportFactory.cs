namespace AssetViewer.ItemViewer;

internal static class ModelViewportFactory
{
    internal static IModelViewport Create() => new Dx12ModelViewportHost();
}
