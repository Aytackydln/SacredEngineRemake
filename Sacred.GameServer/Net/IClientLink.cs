using System.Collections.Concurrent;
using System.Net;

namespace Sacred.GameServer.Net;

/// <summary>
/// One client's message stream, independent of how the bytes travel. The game loop only appends complete TinCat frames
/// (<see cref="BeginFrame"/>/<see cref="CommitFrame"/>), flushes once per tick and closes; inbound frames arrive through the
/// shared inbox as <see cref="InboundFrame"/>s with exactly one <see cref="InboundFrame.Disconnected"/> frame at the end.
/// TCP (<see cref="ClientConnection"/>) is the stream the stock client speaks; reliable UDP streams (KCP, as Sacred Bild uses
/// for LAN play) implement the same contract, so sessions do not care which one a player uses.
/// </summary>
public interface IClientLink
{
    /// <summary>Short transport name for logs ("tcp", "kcp").</summary>
    string Transport { get; }
    IPEndPoint? RemoteEndPoint { get; }
    /// <summary>The server address the client reached (roster record 0).</summary>
    IPEndPoint? LocalEndPoint { get; }
    /// <summary>Game loop owned state.</summary>
    object? Session { get; set; }
    bool IsClosed { get; }

    /// <summary>Reserve <paramref name="bytes"/> for one complete frame; game loop thread only.</summary>
    Span<byte> BeginFrame(int bytes);
    void CommitFrame(int bytes);
    int PendingBytes { get; }
    /// <summary>Hand the frames written since the last flush to the transport.</summary>
    void Flush();
    /// <summary>Closes once; queues the <see cref="InboundFrame.Disconnected"/> frame.</summary>
    void Close();
    /// <summary>Called by the game loop some time after the disconnect frame: return pooled buffers.</summary>
    void ReleaseBuffers();
}

/// <summary>A source of new client links (a TCP acceptor, a KCP endpoint, ...). Links are queued for the game loop.</summary>
public interface IClientListener : IDisposable
{
    string Transport { get; }
    int Port { get; }
    ConcurrentQueue<IClientLink> Accepted { get; }
    void Start();
}
