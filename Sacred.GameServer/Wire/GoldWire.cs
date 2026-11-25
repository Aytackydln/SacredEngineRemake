using System.Buffers.Binary;

namespace Sacred.GameServer.Wire;

/// <summary>A decoded Gold message that points into the batch buffer (no copy, no allocation).</summary>
public readonly ref struct GoldMessageView
{
    public readonly ushort Opcode;
    public readonly byte Sender, Origin;
    public readonly ushort Sequence;
    public readonly ReadOnlySpan<byte> Payload;
    public readonly ReadOnlySpan<byte> Raw;

    public GoldMessageView(ushort opcode, byte sender, byte origin, ushort sequence, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> raw)
    { Opcode = opcode; Sender = sender; Origin = origin; Sequence = sequence; Payload = payload; Raw = raw; }
}

/// <summary>Walks the messages of a decoded batch. <see cref="Failed"/> is set on malformed data.</summary>
public ref struct GoldBatchReader
{
    private ReadOnlySpan<byte> _rest;
    public bool Failed { get; private set; }

    public GoldBatchReader(ReadOnlySpan<byte> batch) { _rest = batch; Failed = false; }

    public bool TryRead(out GoldMessageView message)
    {
        message = default;
        if (_rest.IsEmpty || Failed) return false;
        if (_rest.Length < GoldWire.MessageHeaderBytes) { Failed = true; return false; }
        var word = BinaryPrimitives.ReadUInt32LittleEndian(_rest);
        var size = (int)(word & 0x003FFFFF);
        if (size < GoldWire.MessageHeaderBytes || size > _rest.Length ||
            !_rest.Slice(10, 4).SequenceEqual("LH20"u8)) { Failed = true; return false; }
        var raw = _rest[..size];
        if (BinaryPrimitives.ReadUInt32LittleEndian(raw[4..]) != SacredCrc.ComputeMessage(raw)) { Failed = true; return false; }
        message = new GoldMessageView(BinaryPrimitives.ReadUInt16LittleEndian(raw[14..]), (byte)((word >> 22) & 31), (byte)(word >> 27),
            BinaryPrimitives.ReadUInt16LittleEndian(raw[8..]), raw[GoldWire.MessageHeaderBytes..], raw);
        _rest = _rest[size..];
        return true;
    }
}

/// <summary>Gold message and envelope encoding on caller supplied buffers (same wire format as Sacred.Network.Protocol).</summary>
public static class GoldWire
{
    public const int MessageHeaderBytes = 16;
    public const int EnvelopeHeaderBytes = 10;
    public const int MaximumBatchBytes = 0x3FFFFE;
    public const int MaximumCompressedBytes = 0x3FFFF;
    public const int RawThreshold = 80;

    /// <summary>Writes one complete message (header, checksum, payload copy) and returns its size.</summary>
    public static int WriteMessage(Span<byte> destination, ushort opcode, byte sender, byte origin, ushort sequence, ReadOnlySpan<byte> payload)
    {
        var size = MessageHeaderBytes + payload.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(destination, (uint)size | ((uint)sender << 22) | ((uint)origin << 27));
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[8..], sequence);
        "LH20"u8.CopyTo(destination[10..]);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[14..], opcode);
        payload.CopyTo(destination[MessageHeaderBytes..]);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], SacredCrc.ComputeMessage(destination[..size]));
        return size;
    }

    /// <summary>Re-stamps sender/origin/sequence of an already framed message (relay) and recomputes its checksum.</summary>
    public static void Restamp(Span<byte> message, byte sender, byte origin, ushort sequence)
    {
        var size = (int)(BinaryPrimitives.ReadUInt32LittleEndian(message) & 0x003FFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(message, (uint)size | ((uint)sender << 22) | ((uint)origin << 27));
        BinaryPrimitives.WriteUInt16LittleEndian(message[8..], sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(message[4..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(message[4..], SacredCrc.ComputeMessage(message[..size]));
    }

    private static void WriteEnvelopeHeader(Span<byte> d, int compressed, int unpacked, uint checksum)
    {
        for (var i = 0; i < 5; i++)
            d[i] = (byte)(((compressed >> (4 * i)) & 15) | (((unpacked >> (4 * (i + 1))) & 15) << 4));
        d[5] = (byte)((unpacked & 15) | (((unpacked >> 24) & 15) << 4));
        BinaryPrimitives.WriteUInt32LittleEndian(d[6..], checksum);
    }

    /// <summary>Wrap a batch of messages in the 10-byte envelope; large batches are zlib compressed with a reusable encoder.</summary>
    /// <returns>Envelope size, or -1 when <paramref name="destination"/> is too small.</returns>
    public static int EncodeEnvelope(ReadOnlySpan<byte> batch, Span<byte> destination, ZlibDeflater deflater)
    {
        if (batch.Length is <= 0 or > MaximumBatchBytes) return -1;
        var body = destination[EnvelopeHeaderBytes..];
        if (batch.Length < RawThreshold)
        {
            if (body.Length < batch.Length) return -1;
            var previous = (uint)batch.Length;
            batch.CopyTo(body);
            for (var offset = 0; offset + 4 <= batch.Length; offset += 4)
            {
                var word = BinaryPrimitives.ReadUInt32LittleEndian(body[offset..]);
                var transformed = unchecked(previous + 0xB5D6C7A3) ^ word;
                BinaryPrimitives.WriteUInt32LittleEndian(body[offset..], transformed);
                previous = transformed;
            }
            WriteEnvelopeHeader(destination, 0, batch.Length, SacredCrc.Compute(body[..batch.Length]));
            return EnvelopeHeaderBytes + batch.Length;
        }
        var written = deflater.Compress(batch, body);
        if (written < 0 || written > MaximumCompressedBytes) return -1;
        WriteEnvelopeHeader(destination, written, batch.Length, SacredCrc.Compute(body[..written]));
        return EnvelopeHeaderBytes + written;
    }

    /// <summary>Validate and unpack an envelope into <paramref name="destination"/> (needs the unpacked size).</summary>
    public static bool TryDecodeEnvelope(ReadOnlySpan<byte> envelope, Span<byte> destination, ZlibInflater inflater, out int length)
    {
        length = 0;
        if (envelope.Length < EnvelopeHeaderBytes) return false;
        var compressed = 0;
        var unpacked = (envelope[5] & 15) | ((envelope[5] >> 4) << 24);
        for (var i = 0; i < 5; i++)
        {
            compressed |= (envelope[i] & 15) << (4 * i);
            unpacked |= (envelope[i] >> 4) << (4 * (i + 1));
        }
        if (compressed > MaximumCompressedBytes || unpacked is <= 0 or > MaximumBatchBytes || unpacked > destination.Length) return false;
        var body = envelope[EnvelopeHeaderBytes..];
        if (body.Length != (compressed == 0 ? unpacked : compressed)) return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(envelope[6..]) != SacredCrc.Compute(body)) return false;
        if (compressed == 0)
        {
            var previous = (uint)unpacked;
            body.CopyTo(destination);
            for (var offset = 0; offset + 4 <= unpacked; offset += 4)
            {
                var cipher = BinaryPrimitives.ReadUInt32LittleEndian(destination[offset..]);
                BinaryPrimitives.WriteUInt32LittleEndian(destination[offset..], unchecked(previous + 0xB5D6C7A3) ^ cipher);
                previous = cipher;
            }
            length = unpacked;
            return true;
        }
        if (!inflater.TryInflate(body, destination[..unpacked], out var written) || written != unpacked) return false;
        length = unpacked;
        return true;
    }

    /// <summary>Peek the unpacked size of an envelope header (for sizing the destination buffer).</summary>
    public static int PeekUnpackedSize(ReadOnlySpan<byte> envelope)
    {
        if (envelope.Length < EnvelopeHeaderBytes) return -1;
        var unpacked = (envelope[5] & 15) | ((envelope[5] >> 4) << 24);
        for (var i = 0; i < 5; i++) unpacked |= (envelope[i] >> 4) << (4 * (i + 1));
        return unpacked;
    }
}
