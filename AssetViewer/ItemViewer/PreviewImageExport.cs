using Avalonia.Media.Imaging;

namespace AssetViewer.ItemViewer;

internal static class PreviewImageExport
{
    internal static void Save(Bitmap bitmap, string path) => bitmap.Save(path);
}
