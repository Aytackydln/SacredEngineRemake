using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace Sacred.GameServer.Simulation;

/// <summary>
/// Session capture (docs/authoritative-world-plan.md, M0): every TinCat frame a client sends (login, keep-alives, the game data with
/// admission and every message, the disconnect), with its time and connection number, so real sessions with stock clients can be
/// replayed through the server exactly (<see cref="CaptureReplay"/>). <c>--capture=&lt;folder&gt;</c> writes one file per server run,
/// <c>capture-yyyyMMdd-HHmmss.sgcap</c>. The game loop appends into a pooled buffer; a worker thread writes full buffers, so the
/// loop never touches the disk and allocates nothing in steady state.
/// <para>File: <c>"SGSCAP02"</c>, u64 unix milliseconds of the start, then records
/// <c>[u32 size of the rest][u32 ms since start][u16 connection][u16 0][u32 TinCat frame type][u32 TinCat sender][payload]</c>;
/// frame type <c>0xFFFFFFFF</c> is the disconnect.</para>
/// <para>Privacy: captures hold everything players send, chat and character names included. They stay on the server; the folder gets
/// a <c>.gitignore</c> of <c>*</c>.</para>
/// </summary>
public sealed class CaptureWriter : IDisposable
{
    public const ulong Magic = 0x3230504143534753;        // "SGSCAP02"
    public const int HeaderBytes = 16, RecordHeaderBytes = 20;
    private const int BufferBytes = 1 << 20;

    private readonly FileStream _file;
    private readonly long _startMs;
    private readonly ConcurrentQueue<(byte[] Buffer, int Length)> _pending = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly Thread _worker;
    private volatile bool _stopping;
    private byte[] _buffer = ArrayPool<byte>.Shared.Rent(BufferBytes);
    private int _length;
    private long _lastFlushMs;

    public string Path { get; }
    public long Records { get; private set; }
    public long Bytes { get; private set; }
    /// <summary>Write errors on the worker (the capture stops writing, the server goes on).</summary>
    public int Errors { get; private set; }

    private CaptureWriter(string path, long startMs)
    {
        Path = path;
        _startMs = startMs;
        _file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1 << 16);
        Span<byte> header = stackalloc byte[HeaderBytes];
        BinaryPrimitives.WriteUInt64LittleEndian(header, Magic);
        BinaryPrimitives.WriteInt64LittleEndian(header[8..], DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _file.Write(header);
        _worker = new Thread(Work) { IsBackground = true, Name = "capture" };
        _worker.Start();
    }

    /// <summary>Opens a new capture file in <paramref name="folder"/> (created when missing, with a .gitignore). Null on failure (logged).</summary>
    public static CaptureWriter? Open(string folder, long nowMs)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var ignore = System.IO.Path.Combine(folder, ".gitignore");
            if (!File.Exists(ignore)) File.WriteAllText(ignore, "*\n");
            var path = System.IO.Path.Combine(folder, $"capture-{DateTime.UtcNow:yyyyMMdd-HHmmss}.sgcap");
            for (var i = 1; File.Exists(path); i++) path = System.IO.Path.Combine(folder, $"capture-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{i}.sgcap");
            var writer = new CaptureWriter(path, nowMs);
            Log.Info($"Capture: writing every client frame to {path}.");
            return writer;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Info($"Capture: cannot write to {folder}: {e.Message}; capture is off.");
            return null;
        }
    }

    /// <summary>Game loop: one frame from a client.</summary>
    public void Append(long nowMs, ushort connection, uint type, uint sender, ReadOnlySpan<byte> payload)
    {
        var size = RecordHeaderBytes + payload.Length;
        if (_length + size > _buffer.Length) Hand();
        var target = _buffer;
        var at = _length;
        if (size > target.Length)                                            // a huge upload: its own buffer
        {
            target = ArrayPool<byte>.Shared.Rent(size);
            at = 0;
        }
        var record = target.AsSpan(at, size);
        BinaryPrimitives.WriteInt32LittleEndian(record, size - 4);
        BinaryPrimitives.WriteUInt32LittleEndian(record[4..], (uint)Math.Max(0, nowMs - _startMs));
        BinaryPrimitives.WriteUInt16LittleEndian(record[8..], connection);
        BinaryPrimitives.WriteUInt16LittleEndian(record[10..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(record[12..], type);
        BinaryPrimitives.WriteUInt32LittleEndian(record[16..], sender);
        payload.CopyTo(record[RecordHeaderBytes..]);
        Records++;
        Bytes += size;
        if (!ReferenceEquals(target, _buffer)) { _pending.Enqueue((target, size)); _signal.Release(); }
        else _length += size;
    }

    /// <summary>Game loop, once per tick: hands the buffer to the worker at least once a second.</summary>
    public void Pump(long nowMs)
    {
        if (_length > 0 && nowMs - _lastFlushMs >= 1000) { Hand(); _lastFlushMs = nowMs; }
    }

    private void Hand()
    {
        if (_length == 0) return;
        _pending.Enqueue((_buffer, _length));
        _signal.Release();
        _buffer = ArrayPool<byte>.Shared.Rent(BufferBytes);
        _length = 0;
    }

    private void Work()
    {
        while (true)
        {
            _signal.Wait(500);
            while (_pending.TryDequeue(out var chunk))
            {
                try { if (Errors == 0) _file.Write(chunk.Buffer, 0, chunk.Length); }
                catch (IOException e) { Errors++; Log.Info($"Capture: write failed ({e.Message}); capture stops writing."); }
                ArrayPool<byte>.Shared.Return(chunk.Buffer);
            }
            if (_stopping && _pending.IsEmpty) break;
        }
        try { _file.Flush(true); } catch (IOException) { }
    }

    public void Dispose()
    {
        if (_stopping) return;
        Hand();
        _stopping = true;
        _signal.Release();
        _worker.Join();
        _file.Dispose();
        _signal.Dispose();
    }
}

/// <summary>One captured frame.</summary>
public readonly ref struct CaptureRecord(uint atMs, ushort connection, uint type, uint sender, ReadOnlySpan<byte> payload)
{
    public uint AtMs { get; } = atMs;
    public ushort Connection { get; } = connection;
    /// <summary>TinCat frame type (Net/TinCatWire); <see cref="Net.InboundFrame.Disconnected"/> for the disconnect.</summary>
    public uint Type { get; } = type;
    public uint Sender { get; } = sender;
    public ReadOnlySpan<byte> Payload { get; } = payload;
}

/// <summary>Reads a capture file (tests, replay, tools).</summary>
public sealed class CaptureReader
{
    private readonly byte[] _data;
    private int _at = CaptureWriter.HeaderBytes;

    public long StartUnixMs { get; }

    public CaptureReader(byte[] data)
    {
        if (data.Length < CaptureWriter.HeaderBytes || BinaryPrimitives.ReadUInt64LittleEndian(data) != CaptureWriter.Magic)
            throw new InvalidDataException("not a GameServer capture");
        _data = data;
        StartUnixMs = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(8));
    }

    /// <summary>Reads the whole file; also one a running server is still writing (shared read/write; a cut last record is skipped).</summary>
    public static CaptureReader Open(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var data = new byte[file.Length];
        file.ReadExactly(data);
        return new CaptureReader(data);
    }

    public bool TryRead(out CaptureRecord record)
    {
        record = default;
        if (_at + 4 > _data.Length) return false;
        var size = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(_at)) + 4;
        if (size < CaptureWriter.RecordHeaderBytes || _at + size > _data.Length) return false;
        var r = _data.AsSpan(_at, size);
        record = new CaptureRecord(BinaryPrimitives.ReadUInt32LittleEndian(r[4..]), BinaryPrimitives.ReadUInt16LittleEndian(r[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(r[12..]), BinaryPrimitives.ReadUInt32LittleEndian(r[16..]), r[CaptureWriter.RecordHeaderBytes..]);
        _at += size;
        return true;
    }
}

/// <summary>The last opcodes each player sent, for the witness's log lines ("what happened just before this item appeared").</summary>
public sealed class OpcodeTrail
{
    public const int Length = 16;
    private readonly ushort[,] _opcodes = new ushort[ItemWitness.MaxSlots + 1, Length];
    private readonly long[,] _at = new long[ItemWitness.MaxSlots + 1, Length];
    private readonly int[] _next = new int[ItemWitness.MaxSlots + 1];

    public void Add(byte slot, ushort opcode, long nowMs)
    {
        if (slot is 0 or > ItemWitness.MaxSlots) return;
        var i = _next[slot];
        _opcodes[slot, i] = opcode;
        _at[slot, i] = nowMs + 1;
        _next[slot] = (i + 1) % Length;
    }

    public void Forget(byte slot)
    {
        if (slot is 0 or > ItemWitness.MaxSlots) return;
        for (var i = 0; i < Length; i++) { _opcodes[slot, i] = 0; _at[slot, i] = 0; }
        _next[slot] = 0;
    }

    /// <summary>"0x165 -1200ms, 0x17D -80ms, ..." oldest first (only for log lines).</summary>
    public string Describe(byte slot, long nowMs)
    {
        if (slot is 0 or > ItemWitness.MaxSlots) return "";
        var text = new System.Text.StringBuilder();
        for (var k = 0; k < Length; k++)
        {
            var i = (_next[slot] + k) % Length;
            if (_at[slot, i] == 0) continue;
            if (text.Length > 0) text.Append(", ");
            text.Append("0x").Append(_opcodes[slot, i].ToString("X3")).Append(' ').Append(_at[slot, i] - 1 - nowMs).Append("ms");
        }
        return text.ToString();
    }
}
