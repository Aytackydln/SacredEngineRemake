using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Channels;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.TinCat;

namespace Sacred.Network.Connection;

/// <summary>One Gold gameplay transport. TinCat admission does not assign a Sacred player slot.</summary>
public sealed class GoldGameConnection : IAsyncDisposable
{
    private readonly object _lifetimeGate = new();
    private readonly TcpClient _client = new() { NoDelay = true };
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly Channel<GoldConnectionEvent> _events = Channel.CreateBounded<GoldConnectionEvent>(
        new BoundedChannelOptions(16) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    private Task? _run;
    private NetworkStream? _stream;
    private uint _connectionId;
    private int _started;
    private int _disposed;
    private bool _resourcesDisposed;
    private GoldFirstContact? _firstContact;
    private readonly GoldAdmissionTracker _admission = new();
    private ushort _sendSequence;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public ChannelReader<GoldConnectionEvent> Events => _events.Reader;
    public Task Completion => _run ?? Task.CompletedTask;
    public GoldAdmissionStatus Admission => _admission.Status;

    public void Start(string host, int port) => StartCore(host, port, null);

    /// <summary>Send a complete native FIRSTCONTACT after TinCat login, then track the slot response.</summary>
    public void Start(string host, int port, GoldFirstContact firstContact)
    {
        ArgumentNullException.ThrowIfNull(firstContact);
        StartCore(host, port, firstContact);
    }

    private void StartCore(string host, int port, GoldFirstContact? firstContact)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (port is <= 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("Connection already started.");
            _firstContact = firstContact;
            _run = RunAsync(host, port);
        }
    }

    /// <summary>Caller supplies a verified message; FIRSTCONTACT sessions bind its sender and assign the sequence in wire order.</summary>
    public ValueTask SendAsync(GoldMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(message.Payload);
        if (_firstContact is not null)
        {
            var admission = Admission;
            if (admission.Phase != GoldAdmissionPhase.SlotAssigned)
                throw new InvalidOperationException("Gold slot assignment has not completed.");
            if (message.Sender != admission.PlayerSlot || message.Opcode == GoldFirstContactLayout.Opcode)
                throw new ArgumentException("Message must use the assigned Sacred slot and cannot repeat FIRSTCONTACT.", nameof(message));
        }
        if (_firstContact is not null && message.Opcode == 0x12 && message.Payload.Length == 0)
            throw new ArgumentException("The native client does not submit an empty game password.", nameof(message));
        if (_firstContact is not null && message.Opcode == 0x12 &&
            ((message.Payload.Length & 1) != 0 || message.Payload.Length > GoldPasswordCodec.MaximumPasswordBytes))
            throw new ArgumentException("Game password must contain bounded UTF-16 code units.", nameof(message));
        return SendCoreAsync(message, cancellationToken, passwordRequest: _firstContact is not null && message.Opcode == 0x12);
    }

    /// <summary>Submit the game password after slot assignment; accepted/rejected results arrive through Events.</summary>
    public ValueTask SendPasswordAsync(string password, CancellationToken cancellationToken = default)
    {
        if (_firstContact is null) throw new InvalidOperationException("Password submission requires FIRSTCONTACT negotiation.");
        if (Admission.Phase != GoldAdmissionPhase.SlotAssigned)
            throw new InvalidOperationException("Password submission requires an assigned Sacred slot.");
        var message = GoldPasswordCodec.CreateMessage(password, Admission.PlayerSlot, 0);
        return SendCoreAsync(message, cancellationToken, passwordRequest: true);
    }

    private async ValueTask SendCoreAsync(GoldMessage message, CancellationToken cancellationToken, bool passwordRequest = false)
    {
        CancellationTokenSource linked;
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, cancellationToken);
        }
        using (linked)
        {
            await _sendGate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                if (_firstContact is not null && message.Opcode != GoldFirstContactLayout.Opcode)
                {
                    var status = Admission;
                    if (status.Phase != GoldAdmissionPhase.SlotAssigned || message.Sender != status.PlayerSlot)
                        throw new InvalidOperationException("Message sender no longer has an assigned Sacred slot.");
                    message = message with { Sequence = unchecked((ushort)(_sendSequence + 1)) };
                }
                var body = GoldEnvelopeCodec.Encode(GoldMessageCodec.Encode(message));
                var payload = TinCatCustomData.Encode(TinCatLogin.GameModule, 0, body);
                if (passwordRequest) _admission.BeginPassword();
                if (_firstContact is not null) _sendSequence = message.Sequence;
                try { await WriteFrameAsync(TinCatLogin.CustomData, payload, linked.Token).ConfigureAwait(false); }
                catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
                { Stop(); throw; }
            }
            finally { _sendGate.Release(); }
        }
    }

    public void Stop()
    {
        lock (_lifetimeGate)
        {
            if (_disposed != 0) return;
            _stop.Cancel();
            _client.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        // The engine drains Events on its own thread. Cancellation releases a blocked queue writer.
        Task? run;
        lock (_lifetimeGate)
        {
            if (_disposed == 0)
            {
                _disposed = 1;
                _stop.Cancel();
                _client.Dispose();
            }
            run = _run;
        }
        if (run is not null) await run.ConfigureAwait(false);
        lock (_lifetimeGate)
        {
            if (_resourcesDisposed) return;
            _resourcesDisposed = true;
            _events.Writer.TryComplete();
            _stop.Dispose();
        }
        // No semaphore WaitHandle is created. Leave the managed gate valid for cancelled external sends.
    }

    private async Task RunAsync(string host, int port)
    {
        var reason = "disconnected";
        Task? alive = null;
        using var admissionLimit = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        try
        {
            using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
            {
                handshake.CancelAfter(TimeSpan.FromSeconds(10));
                await _client.ConnectAsync(host, port, handshake.Token).ConfigureAwait(false);
                _stream = _client.GetStream();
                await TinCatStream.WriteAsync(_stream, TinCatLogin.CreateRequest("user"), handshake.Token).ConfigureAwait(false);
                // TinCat may send time alignment (type 1) before LOGONACCEPTED.
                TinCatFrame acceptance;
                do { acceptance = await TinCatStream.ReadAsync(_stream, handshake.Token).ConfigureAwait(false); }
                while (acceptance.Type == 1 && acceptance.Sender == TinCatLogin.ServerId);
                _connectionId = TinCatLogin.ReadAccepted(acceptance);
            }
            await _events.Writer.WriteAsync(new GoldTransportConnected(_connectionId), _stop.Token).ConfigureAwait(false);
            if (_firstContact is { } firstContact)
            {
                admissionLimit.CancelAfter(TimeSpan.FromSeconds(10));
                _admission.Begin();
                await SendCoreAsync(firstContact.CreateMessage(), admissionLimit.Token).ConfigureAwait(false);
                await _events.Writer.WriteAsync(new GoldFirstContactSent(), _stop.Token).ConfigureAwait(false);
            }
            alive = KeepAliveAsync();
            while (!_stop.IsCancellationRequested)
            {
                var frame = await TinCatStream.ReadAsync(_stream!, admissionLimit.Token).ConfigureAwait(false);
                if (frame.Sender != TinCatLogin.ServerId || frame.Recipient != _connectionId)
                    throw new InvalidDataException("TinCat frame identity mismatch.");
                switch (frame.Type)
                {
                    case TinCatLogin.CustomData:
                        var messages = DecodeMessages(frame.Payload);
                        if (_firstContact is not null)
                        {
                            _admission.ProcessBatch(messages);
                            if (Admission.Phase != GoldAdmissionPhase.WaitingForResponse)
                                admissionLimit.CancelAfter(Timeout.InfiniteTimeSpan);
                            if (Admission.Phase == GoldAdmissionPhase.SlotAssigned)
                                foreach (var message in messages)
                                    if (message.Opcode == GoldTimeProbeCodec.RequestOpcode)
                                    {
                                        var probe = (GoldTimeProbeEvent)GoldGameEventDecoder.Decode(message);
                                        var reply = GoldTimeProbeCodec.CreateReply(probe, Admission.PlayerSlot, unchecked((uint)_clock.ElapsedMilliseconds));
                                        await SendCoreAsync(reply, admissionLimit.Token).ConfigureAwait(false);
                                    }
                        }
                        await _events.Writer.WriteAsync(new GoldMessagesReceived(messages), _stop.Token).ConfigureAwait(false);
                        if (Admission.Phase == GoldAdmissionPhase.Rejected)
                        {
                            reason = $"Sacred rejected connection: reason {Admission.RejectionReason}, data mismatch mask 0x{Admission.DataMismatchMask:X8}";
                            return;
                        }
                        break;
                    case 1: // Native time alignment and keepalive have no gameplay payload.
                    case TinCatLogin.StayingAlive:
                        break;
                    case 6:
                        reason = "server requested logoff";
                        return;
                    default:
                        throw new InvalidDataException($"Unsupported TinCat control type {frame.Type}.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            reason = _stop.IsCancellationRequested ? "disconnected" :
                Admission.Phase == GoldAdmissionPhase.WaitingForResponse ? "Sacred FIRSTCONTACT timed out" : "connection/login timed out";
        }
        catch (Exception e) when (e is SocketException or IOException or InvalidDataException or ObjectDisposedException)
        { reason = _stop.IsCancellationRequested ? "disconnected" : e.Message; }
        finally
        {
            _stop.Cancel();
            _client.Dispose();
            if (alive is not null) await alive.ConfigureAwait(false);
            _events.Writer.TryWrite(new GoldConnectionClosed(reason));
            _events.Writer.TryComplete();
        }
    }

    private static IReadOnlyList<GoldMessage> DecodeMessages(byte[] payload) =>
        GoldMessageCodec.DecodeBatch(GoldEnvelopeCodec.Decode(TinCatCustomData.Decode(payload, TinCatLogin.GameModule, 0)));

    private async Task KeepAliveAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
                await WriteAsync(TinCatLogin.StayingAlive, new byte[2], _stop.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        { _stop.Cancel(); _client.Dispose(); }
    }

    private async ValueTask WriteAsync(uint type, byte[] payload, CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteFrameAsync(type, payload, cancellationToken).ConfigureAwait(false);
        }
        finally { _sendGate.Release(); }
    }

    private ValueTask WriteFrameAsync(uint type, byte[] payload, CancellationToken cancellationToken)
    {
        if (_stream is null || _connectionId == 0) throw new InvalidOperationException("TinCat login has not completed.");
        return TinCatStream.WriteAsync(_stream, new TinCatFrame(_connectionId, TinCatLogin.ServerId, type, 0, payload), cancellationToken);
    }
}
