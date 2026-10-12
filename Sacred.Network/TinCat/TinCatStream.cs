using System.Net.Sockets;

namespace Sacred.Network.TinCat;

/// <summary>Reads complete frames even when TCP splits or coalesces writes.</summary>
public static class TinCatStream
{
    public static async ValueTask<TinCatFrame> ReadAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var header = new byte[TinCatFrameCodec.HeaderBytes];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var size = TinCatFrameCodec.ReadPayloadLength(header);
        var bytes = new byte[TinCatFrameCodec.HeaderBytes + size];
        header.CopyTo(bytes, 0);
        await stream.ReadExactlyAsync(bytes.AsMemory(header.Length), cancellationToken).ConfigureAwait(false);
        return TinCatFrameCodec.Decode(bytes);
    }

    public static ValueTask WriteAsync(NetworkStream stream, TinCatFrame frame, CancellationToken cancellationToken) =>
        stream.WriteAsync(TinCatFrameCodec.Encode(frame), cancellationToken);
}
