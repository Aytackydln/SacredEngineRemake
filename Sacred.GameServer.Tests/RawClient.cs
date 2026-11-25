using System.Buffers.Binary;
using System.Net.Sockets;
using Sacred.GameServer.Wire;
using Sacred.Network.Admission;

namespace Sacred.GameServer.Tests;

public sealed record RawMessage(ushort Opcode, byte Sender, byte Origin, ushort Sequence, byte[] Payload);

/// <summary>Minimal synchronous protocol client for server tests (no strict client-side decoding).</summary>
public sealed class RawClient : IDisposable
{
    private readonly TcpClient _tcp = new() { NoDelay = true };
    private readonly NetworkStream _stream;
    private readonly ZlibDeflater _deflater = new();
    private readonly ZlibInflater _inflater = new();
    private readonly Queue<RawMessage> _pending = new();
    private uint _id;
    private ushort _sequence;
    public byte Slot { get; private set; }
    public byte AdmissionFlags { get; private set; }

    public RawClient(int port)
    {
        _tcp.Connect("127.0.0.1", port);
        _stream = _tcp.GetStream();
        _stream.ReadTimeout = 3000;
        var login = new byte[52];
        BinaryPrimitives.WriteUInt32LittleEndian(login, TinCatWire.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(login.AsSpan(4), TinCatWire.UnassignedId);
        "user"u8.CopyTo(login.AsSpan(8));
        WriteFrame(TinCatWire.UnassignedId, TinCatWire.LoginRequest, login);
        var (type, payload) = ReadFrame();
        Assert.AreEqual(TinCatWire.LoginAccepted, type);
        _id = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(4));
    }

    public static RawClient Admit(int port, string name, int identity, byte block = 0)
    {
        var client = new RawClient(port);
        var contact = new byte[GoldFirstContactLayout.Size];
        BinaryPrimitives.WriteInt32LittleEndian(contact, identity);
        contact[6] = block;
        System.Text.Encoding.Unicode.GetBytes(name, contact.AsSpan(GoldFirstContactLayout.NameOffset));
        client.Send(0x10, contact, sender: 0);
        var reply = client.WaitFor(0x11);
        client.AdmissionFlags = reply.Payload[0];
        client.Slot = reply.Payload[1];
        return client;
    }

    /// <summary>Sends FIRSTCONTACT and returns the 0x3F rejection reason (fails if the client is admitted).</summary>
    public static uint AdmitExpectingRejection(int port, string name, int identity)
    {
        using var client = new RawClient(port);
        var contact = new byte[GoldFirstContactLayout.Size];
        BinaryPrimitives.WriteInt32LittleEndian(contact, identity);
        System.Text.Encoding.Unicode.GetBytes(name, contact.AsSpan(GoldFirstContactLayout.NameOffset));
        client.Send(0x10, contact, sender: 0);
        return BinaryPrimitives.ReadUInt32LittleEndian(client.WaitFor(0x3F).Payload);
    }

    private void WriteFrame(uint sender, uint type, ReadOnlySpan<byte> payload)
    {
        var frame = new byte[TinCatWire.HeaderBytes + payload.Length];
        payload.CopyTo(frame.AsSpan(TinCatWire.HeaderBytes));
        TinCatWire.SealFrame(frame, sender, TinCatWire.ServerId, type, payload.Length);
        _stream.Write(frame);
    }

    private (uint Type, byte[] Payload) ReadFrame()
    {
        var header = new byte[TinCatWire.HeaderBytes];
        _stream.ReadExactly(header);
        var payload = new byte[BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(20))];
        _stream.ReadExactly(payload);
        return (BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12)), payload);
    }

    public void Send(ushort opcode, ReadOnlySpan<byte> payload, byte? sender = null)
    {
        var slot = sender ?? Slot;
        var message = new byte[16 + payload.Length];
        GoldWire.WriteMessage(message, opcode, slot, slot, _sequence++, payload);
        var envelope = new byte[message.Length * 2 + 64];
        var size = GoldWire.EncodeEnvelope(message, envelope, _deflater);
        var body = new byte[4 + size];
        BinaryPrimitives.WriteUInt16LittleEndian(body, TinCatWire.GameModule);
        envelope.AsSpan(0, size).CopyTo(body.AsSpan(4));
        WriteFrame(_id, TinCatWire.CustomData, body);
    }

    public RawMessage? TryReceive(int timeoutMs)
    {
        if (_pending.Count > 0) return _pending.Dequeue();
        _stream.ReadTimeout = timeoutMs;
        try
        {
            while (_pending.Count == 0)
            {
                var (type, payload) = ReadFrame();
                if (type != TinCatWire.CustomData) continue;
                var batch = new byte[GoldWire.PeekUnpackedSize(payload.AsSpan(4))];
                Assert.IsTrue(GoldWire.TryDecodeEnvelope(payload.AsSpan(4), batch, _inflater, out var length));
                var reader = new GoldBatchReader(batch.AsSpan(0, length));
                while (reader.TryRead(out var m)) _pending.Enqueue(new(m.Opcode, m.Sender, m.Origin, m.Sequence, m.Payload.ToArray()));
            }
            return _pending.Dequeue();
        }
        catch (IOException) { return null; }
    }

    /// <summary>Wait for a message with the opcode, skipping others.</summary>
    public RawMessage WaitFor(ushort opcode, int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
            if (TryReceive((int)Math.Max(1, deadline - Environment.TickCount64)) is { } m && m.Opcode == opcode) return m;
        throw new AssertFailedException($"no 0x{opcode:X3} within {timeoutMs} ms");
    }

    /// <summary>True when no message with that opcode arrives in the window.</summary>
    public bool Silent(ushort opcode, int windowMs = 300)
    {
        var deadline = Environment.TickCount64 + windowMs;
        while (Environment.TickCount64 < deadline)
            if (TryReceive((int)Math.Max(1, deadline - Environment.TickCount64)) is { } m && m.Opcode == opcode) return false;
        return true;
    }

    public bool IsClosed()
    {
        _stream.ReadTimeout = 2000;
        try { while (true) { var (_, _) = ReadFrame(); } }
        catch (EndOfStreamException) { return true; }
        catch (IOException e) { return e.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset }; }
    }

    public void Dispose() => _tcp.Dispose();
}
