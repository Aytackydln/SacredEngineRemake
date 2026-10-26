using System.Runtime.InteropServices.JavaScript;
using Avalonia.Media.Imaging;

namespace AssetViewer.ItemViewer;

internal static partial class PreviewImageExport
{
    internal static void Save(Bitmap bitmap, string path)
    {
        using var buffer = new MemoryStream(); bitmap.Save(buffer);
        Download(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)), Path.GetFileName(path));
    }

    [JSImport("downloadBytes", "webgpu")]
    private static partial void Download([JSMarshalAs<JSType.MemoryView>] Span<byte> png, string name);
}
