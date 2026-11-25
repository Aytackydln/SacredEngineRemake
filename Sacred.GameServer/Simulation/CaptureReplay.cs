using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Sacred.GameServer.Auth;
using Sacred.GameServer.Config;
using Sacred.GameServer.Net;
using Sacred.GameServer.Session;

namespace Sacred.GameServer.Simulation;

/// <summary>What a replay found (docs/authoritative-world-plan.md, M0).</summary>
public sealed record ReplayReport(int Connections, long Frames, long DurationMs, long Ticks, int[] Explained, int Refused, int ExplainedLate,
    int GoldUnexplained, long GoldUnexplainedAmount, int GoldClamped, int RejectedItems, long ServerFrames, int KillsAlive = 0, long Positions = 0, long HitPointUpdates = 0,
    IReadOnlyList<(long AtMs, int Refused)>? Bursts = null, IReadOnlyList<(int Opcode, long Count)>? Opcodes = null)
{
    public string Describe()
    {
        var text = new StringBuilder();
        text.AppendLine($"Replay: {Connections} connections, {Frames} frames, {(DurationMs / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} s, {Ticks} ticks, {ServerFrames} frames sent by the server.");
        text.AppendLine("Item creations by source:");
        for (var i = 0; i < Explained.Length; i++)
            if (Explained[i] > 0 || i == 0) text.AppendLine($"  {(ItemSource)i,-12} {Explained[i]}");
        text.AppendLine($"Refused by the strict item gate: {Refused}; unexplained items an event explained later: {ExplainedLate}; impossible item states: {RejectedItems}.");
        text.AppendLine($"Gold: {GoldUnexplained} unexplained increases ({GoldUnexplainedAmount} gold), {GoldClamped} not saved.");
        if (Bursts is { Count: > 0 })
        {
            text.Append("Unexplained creations over time (s:count):");
            foreach (var (at, n) in Bursts) text.Append(' ').Append(at / 1000).Append(':').Append(n);
            text.AppendLine();
        }
        if (Opcodes is { Count: > 0 })
        {
            text.Append("Messages from the players by opcode:");
            foreach (var (op, n) in Opcodes) text.Append(" 0x").Append(op.ToString("X3")).Append(':').Append(n);
            text.AppendLine();
        }
        text.AppendLine($"World: {Positions} position and {HitPointUpdates} health updates applied; {KillsAlive} kills of creatures that still had hit points.");
        return text.ToString();
    }
}

/// <summary>
/// Replays a capture (<see cref="CaptureWriter"/>) through a real <see cref="GameServerHost"/>: each captured connection becomes an
/// in-memory link, the frames go into the server's inbox at their captured time, and the server's clock is moved tick by tick, so the
/// server makes the decisions it would have made in that session, with the strictness settings of <paramref name="config"/>.
/// Nothing is sent anywhere: the server's answers are counted and dropped. There is no lobby, so the Closed Net hero custody the
/// strict checks need is assumed for every admitted hero (no baseline block: items the hero brought along are unexplained unless
/// another source covers them). Command line: <c>--replay=&lt;capture file&gt;</c>.
/// </summary>
public static class CaptureReplay
{
    public static ReplayReport Run(GameServerConfig config, CaptureReader reader)
    {
        config.TcpPort = 0;
        config.BindAddress = IPAddress.Loopback;
        config.LanAnnounce = false;
        config.UdpPort = 0;
        config.LobbyHost = null;
        config.CaptureDirectory = null;
        config.Auth.Enabled = true;
        var clock = new ManualClock();
        ReplayListener? listener = null;
        var host = new GameServerHost(config, new AdmissionRules(null), null, clock) { AssumeHeroCustody = true };
        if (config.GameDirectory is { } gameDir && config.Auth.Witness.PriceCheck != Auth.StrictItemsMode.Off) host.ItemPrices = Auth.ItemPrices.Load(gameDir, out _);
        host.AddListener(inbox => listener = new ReplayListener(inbox));
        var links = new Dictionary<ushort, ReplayLink>();
        long frames = 0, ticks = 0;
        var bursts = new List<(long, int)>();
        var seen = 0;
        void Burst()
        {
            var n = host.StrictItemsRefused - seen;
            if (n == 0) return;
            seen = host.StrictItemsRefused;
            var bucket = clock.Now / 1000 * 1000;
            if (bursts.Count > 0 && bursts[^1].Item1 == bucket) bursts[^1] = (bucket, bursts[^1].Item2 + n);
            else bursts.Add((bucket, n));
        }
        var tick = Math.Max(1, config.TickMilliseconds);
        uint last = 0;
        try
        {
            while (reader.TryRead(out var record))
            {
                while (clock.Now + tick <= record.AtMs) { clock.Now += tick; host.RunTick(); ticks++; Burst(); }
                last = record.AtMs;
                frames++;
                if (!links.TryGetValue(record.Connection, out var link))
                {
                    if (record.Type == InboundFrame.Disconnected) continue;
                    link = new ReplayLink(listener!.Inbox);
                    links.Add(record.Connection, link);
                    listener.Accepted.Enqueue(link);
                }
                if (record.Type == InboundFrame.Disconnected) { link.Close(); continue; }
                if (link.IsClosed) continue;                                // the server closed it (kick); the client's later frames went nowhere
                var buffer = record.Payload.Length == 0 ? Array.Empty<byte>() : ArrayPool<byte>.Shared.Rent(record.Payload.Length);
                record.Payload.CopyTo(buffer);
                listener!.Inbox.Enqueue(new InboundFrame(link, record.Sender, record.Type, buffer, record.Payload.Length));
            }
            for (var i = 0; i < 5; i++) { clock.Now += tick; host.RunTick(); ticks++; Burst(); }
            foreach (var link in links.Values) link.Close();
            for (var i = 0; i < 5; i++) { clock.Now += tick; host.RunTick(); ticks++; }
            var witness = host.Witness!;
            return new ReplayReport(links.Count, frames, last, ticks, (int[])witness.Explained.Clone(), host.StrictItemsRefused, witness.ExplainedLate,
                host.Gold!.Unexplained, host.Gold.UnexplainedGold, host.GoldClamped, host.RejectedItems, links.Values.Sum(l => l.FramesSent),
                host.KillsAlive, host.WorldPositions, host.WorldHitPointUpdates, bursts,
                host.ReceivedByOpcode.Select((n, op) => (op, n)).Where(x => x.n > 0).OrderByDescending(x => x.n).ToList());
        }
        finally { host.Dispose(); }
    }

    private sealed class ReplayListener(ConcurrentQueue<InboundFrame> inbox) : IClientListener
    {
        public ConcurrentQueue<InboundFrame> Inbox { get; } = inbox;
        public string Transport => "replay";
        public int Port => 0;
        public ConcurrentQueue<IClientLink> Accepted { get; } = new();
        public void Start() { }
        public void Dispose() { }
    }

    /// <summary>An in-memory link: frames the server writes are counted and dropped.</summary>
    private sealed class ReplayLink(ConcurrentQueue<InboundFrame> inbox) : IClientLink
    {
        private byte[] _scratch = new byte[64 * 1024];
        public string Transport => "replay";
        public IPEndPoint? RemoteEndPoint { get; } = new(IPAddress.Loopback, 1);
        public IPEndPoint? LocalEndPoint { get; } = new(IPAddress.Loopback, 2);
        public object? Session { get; set; }
        public bool IsClosed { get; private set; }
        public long FramesSent { get; private set; }
        public int PendingBytes => 0;

        public Span<byte> BeginFrame(int bytes)
        {
            if (_scratch.Length < bytes) _scratch = new byte[Math.Max(bytes, _scratch.Length * 2)];
            return _scratch.AsSpan(0, bytes);
        }

        public void CommitFrame(int bytes) => FramesSent++;
        public void Flush() { }

        public void Close()
        {
            if (IsClosed) return;
            IsClosed = true;
            inbox.Enqueue(new InboundFrame(this, 0, InboundFrame.Disconnected, [], 0));
        }

        public void ReleaseBuffers() { }
    }
}
