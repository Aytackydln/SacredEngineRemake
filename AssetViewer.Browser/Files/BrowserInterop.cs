using System.Runtime.InteropServices.JavaScript;

namespace AssetViewer.Browser.Files;

internal static partial class BrowserInterop
{
    [JSImport("pickInstallation", "files")]
    internal static partial Task<string> PickInstallationAsync();

    [JSImport("readRange", "files")]
    private static partial Task<JSObject> ReadRangeObjectAsync(int id, double offset, int count);

    [JSImport("readRanges", "files")]
    private static partial Task<JSObject> ReadRangesObjectAsync(int id, double[] ranges);

    [JSImport("copyBytes", "files")]
    internal static partial void CopyBytes(JSObject source, [JSMarshalAs<JSType.MemoryView>] Span<byte> destination);

    internal static async Task<byte[]> ReadRangeAsync(int id, double offset, int count)
    {
        using var source = await ReadRangeObjectAsync(id, offset, count);
        return ToBytes(source);
    }

    internal static async Task<int> ReadRangeIntoAsync(int id, double offset, Memory<byte> destination)
    {
        using var source = await ReadRangeObjectAsync(id, offset, destination.Length);
        var count = source.GetPropertyAsInt32("byteLength");
        if (count > destination.Length) throw new InvalidDataException("Browser range exceeded its destination.");
        CopyBytes(source, destination.Span[..count]);
        return count;
    }

    internal static byte[] ToBytes(JSObject source)
    {
        var result = new byte[source.GetPropertyAsInt32("byteLength")];
        CopyBytes(source, result);
        return result;
    }

    internal static async Task ReadRangesIntoAsync(int id, double[] ranges, Memory<byte> destination)
    {
        using var source = await ReadRangesObjectAsync(id, ranges);
        if (source.GetPropertyAsInt32("byteLength") != destination.Length)
            throw new EndOfStreamException("The browser returned an incomplete metadata batch.");
        CopyBytes(source, destination.Span);
    }

    [JSImport("releaseInstallation", "files")]
    internal static partial void ReleaseInstallation(string installation);
}
