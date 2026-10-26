namespace AssetViewer.Browser.Files;

/// <summary>Models a sparse five-gigabyte Blob without allocating it.</summary>
internal static class BrowserInterop
{
    internal static readonly List<(long Offset, int Count, bool Direct)> Reads = [];
    internal static readonly List<double[]> Batches = [];
    internal static Task ReadRangesIntoAsync(int id, double[] ranges, Memory<byte> destination)
    {
        Batches.Add(ranges);
        var cursor = 0;
        for (var index = 0; index < ranges.Length; index += 2)
        {
            var count = checked((int)ranges[index + 1]);
            Fill(destination.Span.Slice(cursor, count), checked((long)ranges[index]));
            cursor += count;
        }
        return Task.CompletedTask;
    }
    internal static Task<byte[]> ReadRangeAsync(int id, double offset, int count)
    {
        Reads.Add((checked((long)offset), count, false));
        var bytes = new byte[count]; Fill(bytes, checked((long)offset)); return Task.FromResult(bytes);
    }
    internal static Task<int> ReadRangeIntoAsync(int id, double offset, Memory<byte> destination)
    {
        Reads.Add((checked((long)offset), destination.Length, true));
        Fill(destination.Span, checked((long)offset)); return Task.FromResult(destination.Length);
    }
    private static void Fill(Span<byte> bytes, long offset)
    { for (var index = 0; index < bytes.Length; index++) bytes[index] = (byte)(offset + index); }
}
