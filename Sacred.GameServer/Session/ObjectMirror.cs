using System.Buffers;
using System.Buffers.Binary;

namespace Sacred.GameServer.Session;

/// <summary>
/// The server's copy of the world objects, kept from the object state messages players send (0x169/0x16B/0x16F with a
/// 24-byte prefix, 0x16C/0x16E with an 8-byte prefix: [object ref][object type]([x][y][surface][sector])) and removals
/// (0x166/0x167). GameServer.exe holds real objects (manager at 0x5DB4A0) and re-serializes them; this mirror keeps the
/// latest serialized state per object, which is what its streamer <c>FUN_004cd040</c> sends to players that do not know an
/// object yet (as 0x16F, or 0x16E when the position fields are zero). Buffers are pooled and reused in place.
/// </summary>
public sealed class ObjectMirror
{
    public const int MaxObjectBytes = 64 * 1024;
    private const uint ReferenceMask = 0x7FFFFFFF;

    public sealed class Entry
    {
        public byte[] Buffer = [];
        public int Length;
        public bool LongPrefix;    // 24-byte prefix (0x169/0x16B/0x16F)
        public byte Owner;
        public ushort KnownBy;     // slot bits (bit slot-1) of players that have the object (+0x3A in the original)
        public ReadOnlySpan<byte> Payload => Buffer.AsSpan(0, Length);
    }

    private readonly Dictionary<uint, Entry> _objects;
    private readonly Stack<Entry> _free = new();
    private readonly int _capacity;

    public ObjectMirror(int capacity)
    {
        _capacity = capacity;
        _objects = new Dictionary<uint, Entry>(Math.Min(capacity, 1 << 16));
    }

    public int Count => _objects.Count;
    public Dictionary<uint, Entry>.ValueCollection Objects => _objects.Values;

    public static bool IsState(ushort opcode) => opcode is 0x169 or 0x16B or 0x16C or 0x16E or 0x16F;
    public static bool IsRemoval(ushort opcode) => opcode is 0x166 or 0x167;

    /// <summary>Stores an object state; <paramref name="knownBy"/> are the players that receive it with this message.</summary>
    public bool Store(ushort opcode, byte owner, ReadOnlySpan<byte> payload, ushort knownBy)
    {
        var longPrefix = opcode is 0x169 or 0x16B or 0x16F;
        if (payload.Length < (longPrefix ? 24 : 8) || payload.Length > MaxObjectBytes) return false;
        var reference = BinaryPrimitives.ReadUInt32LittleEndian(payload) & ReferenceMask;
        if (!_objects.TryGetValue(reference, out var entry))
        {
            if (_objects.Count >= _capacity) return false;
            entry = _free.Count > 0 ? _free.Pop() : new Entry();
            _objects.Add(reference, entry);
            entry.KnownBy = 0;
        }
        if (entry.Buffer.Length < payload.Length)
        {
            if (entry.Buffer.Length > 0) ArrayPool<byte>.Shared.Return(entry.Buffer);
            entry.Buffer = ArrayPool<byte>.Shared.Rent(payload.Length);
        }
        payload.CopyTo(entry.Buffer);
        entry.Length = payload.Length;
        entry.LongPrefix = longPrefix;
        entry.Owner = owner;
        entry.KnownBy |= knownBy;
        return true;
    }

    public bool TryGet(uint reference, out Entry entry) => _objects.TryGetValue(reference, out entry!);

    public int CopyReferences(Span<uint> destination)
    {
        var n = 0;
        foreach (var key in _objects.Keys) destination[n++] = key;
        return n;
    }

    public void Remove(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4) return;
        var reference = BinaryPrimitives.ReadUInt32LittleEndian(payload) & ReferenceMask;
        if (_objects.Remove(reference, out var entry)) _free.Push(entry);
    }

    /// <summary>A player left: nobody streams to that slot until it is taken again.</summary>
    public void Forget(byte slot)
    {
        var bit = (ushort)~(1 << (slot - 1));
        foreach (var entry in _objects.Values) entry.KnownBy &= bit;
    }

    public void Clear()
    {
        foreach (var entry in _objects.Values) _free.Push(entry);
        _objects.Clear();
    }

    /// <summary>Offsets inside an 8-byte-prefix state (0x16E): [ref][type] then the object's own serialization
    /// [0xFEEDF00D][ref][type][flags u32][sector u16][x u32][y u32]... (run 8: five carried/placed items).</summary>
    public const int ShortFlags = 20, ShortSector = 24, ShortX = 26, ShortY = 30, ShortMinimum = 34;

    /// <summary>Sector word of a stored state: from the 24-byte prefix ([ref][type][x][y][surface][sector]) or from the
    /// object data of a 0x16E state; 0 when unknown.</summary>
    public static ushort Sector(Entry entry)
    {
        var p = entry.Payload;
        if (entry.LongPrefix) return p.Length >= 24 ? BinaryPrimitives.ReadUInt16LittleEndian(p[20..]) : (ushort)0;
        return p.Length >= ShortMinimum && BinaryPrimitives.ReadUInt32LittleEndian(p[8..]) == 0xFEEDF00D
            ? BinaryPrimitives.ReadUInt16LittleEndian(p[ShortSector..]) : (ushort)0;
    }

    /// <summary>The opcode the original streamer uses: 0x16F with a position, 0x16E (8-byte prefix) without one.</summary>
    public static ushort StreamOpcode(Entry entry, out int prefix, out bool hasPosition)
    {
        hasPosition = false;
        if (entry.LongPrefix)
        {
            var p = entry.Payload;
            hasPosition = BinaryPrimitives.ReadUInt32LittleEndian(p[8..]) != 0 || BinaryPrimitives.ReadUInt32LittleEndian(p[12..]) != 0 || p[16] != 0;
        }
        prefix = entry.LongPrefix ? 24 : 8;
        return hasPosition ? (ushort)0x16F : (ushort)0x16E;
    }
}
