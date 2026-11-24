using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using Sacred.GameServer.Config;
using Sacred.GameServer.Net;
using Sacred.GameServer.Session;
using Sacred.GameServer.Wire;
using Sacred.Network.Admission;

namespace Sacred.GameServer.Tests;

/// <summary>Sessions run on any <see cref="IClientLink"/>: an in-memory transport added with AddListener logs in like TCP.</summary>
[TestClass]
public class TransportTests
{
    private sealed class MemoryLink(ConcurrentQueue<InboundFrame> inbox) : IClientLink
    {
        private readonly byte[] _fill = new byte[64 * 1024];
        private int _fillLength;
        private int _closed;
        public readonly ConcurrentQueue<byte[]> Sent = new();
        public string Transport => "memory";
        public IPEndPoint? RemoteEndPoint { get; } = new(IPAddress.Loopback, 40000);
        public IPEndPoint? LocalEndPoint { get; } = new(IPAddress.Loopback, 2006);
        public object? Session { get; set; }
        public bool IsClosed => Volatile.Read(ref _closed) != 0;
        public Span<byte> BeginFrame(int bytes) => _fill.AsSpan(_fillLength, bytes);
        public void CommitFrame(int bytes) => _fillLength += bytes;
        public int PendingBytes => _fillLength;
        public void Flush() { if (_fillLength == 0) return; Sent.Enqueue(_fill.AsSpan(0, _fillLength).ToArray()); _fillLength = 0; }
        public void Close() { if (Interlocked.Exchange(ref _closed, 1) == 0) inbox.Enqueue(new InboundFrame(this, 0, InboundFrame.Disconnected, [], 0)); }
        public void ReleaseBuffers() { }

        public void Receive(uint sender, uint type, ReadOnlySpan<byte> payload)
        {
            var rented = ArrayPool<byte>.Shared.Rent(payload.Length);
            payload.CopyTo(rented);
            inbox.Enqueue(new InboundFrame(this, sender, type, rented, payload.Length));
        }
    }

    private sealed class MemoryListener(ConcurrentQueue<InboundFrame> inbox) : IClientListener
    {
        public string Transport => "memory";
        public int Port => 0;
        public ConcurrentQueue<IClientLink> Accepted { get; } = new();
        public void Start() { }
        public void Dispose() { }
        public MemoryLink Connect() { var link = new MemoryLink(inbox); Accepted.Enqueue(link); return link; }
    }

    [TestMethod]
    public void LoginWorksOverAnAddedTransport()
    {
        var host = new GameServerHost(new GameServerConfig { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5 }, new AdmissionRules(null), null);
        var listener = (MemoryListener)host.AddListener(inbox => new MemoryListener(inbox));
        var thread = new Thread(host.Run) { IsBackground = true };
        thread.Start();
        try
        {
            var link = listener.Connect();
            var login = new byte[TinCatWire.LoginPayloadBytes];
            BinaryPrimitives.WriteUInt32LittleEndian(login, TinCatWire.Magic);
            BinaryPrimitives.WriteUInt32LittleEndian(login.AsSpan(4), TinCatWire.UnassignedId);
            link.Receive(TinCatWire.UnassignedId, TinCatWire.LoginRequest, login);
            var deadline = Environment.TickCount64 + 3000;
            byte[]? frame = null;
            while (Environment.TickCount64 < deadline && !link.Sent.TryDequeue(out frame)) Thread.Sleep(5);
            Assert.IsNotNull(frame, "no answer over the memory link");
            Assert.AreEqual(TinCatWire.Magic, BinaryPrimitives.ReadUInt32LittleEndian(frame));
            Assert.AreEqual(TinCatWire.LoginAccepted, BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(12)));
        }
        finally { host.Stop(); thread.Join(); host.Dispose(); }
    }
}
