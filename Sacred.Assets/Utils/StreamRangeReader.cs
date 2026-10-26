namespace Sacred.Assets.Utils;

/// <summary>Reads compact metadata from sparse archive ranges. Ordinary streams keep their sequential I/O path.</summary>
public static class StreamRangeReader
{
    public const int MaxBatchRanges = 256;
    public const int MaxBatchBytes = 64 * 1024;

    public static async Task<byte[]> ReadAsync(Stream stream, StreamReadRange[] ranges,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(ranges);
        cancellationToken.ThrowIfCancellationRequested();
        var total = 0;
        foreach (var range in ranges)
        {
            if (range.Offset < 0 || range.Length < 0 || range.Offset > stream.Length - range.Length)
                throw new EndOfStreamException("An indexed range exceeds the archive.");
            total = checked(total + range.Length);
        }
        var bytes = new byte[total];
        var destinationOffset = 0;
        for (var start = 0; start < ranges.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var end = start;
            var size = 0;
            while (end < ranges.Length && end - start < MaxBatchRanges && ranges[end].Length <= MaxBatchBytes - size)
                size += ranges[end++].Length;
            if (end == start) { end++; size = ranges[start].Length; }
            if (stream is IBatchedReadStream batched && size <= MaxBatchBytes)
                await batched.ReadRangesAsync(ranges.AsMemory(start, end - start), bytes.AsMemory(destinationOffset, size), cancellationToken)
                    .ConfigureAwait(false);
            else
            {
                var offset = destinationOffset;
                for (var index = start; index < end; index++)
                {
                    var range = ranges[index];
                    stream.Position = range.Offset;
                    await stream.ReadExactlyAsync(bytes.AsMemory(offset, range.Length), cancellationToken).ConfigureAwait(false);
                    offset += range.Length;
                }
            }
            destinationOffset += size;
            start = end;
        }
        return bytes;
    }
}
