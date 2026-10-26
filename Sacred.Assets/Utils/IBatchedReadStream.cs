namespace Sacred.Assets.Utils;

/// <summary>Optional asynchronous scatter reads for streams with expensive individual seeks.
/// Concatenates exact ranges in request order without changing Stream.Position.
/// Callers must not overlap reads with disposal. Cancellation may leave destination partially written.</summary>
public interface IBatchedReadStream
{
    ValueTask ReadRangesAsync(ReadOnlyMemory<StreamReadRange> ranges, Memory<byte> destination,
        CancellationToken cancellationToken = default);
}
