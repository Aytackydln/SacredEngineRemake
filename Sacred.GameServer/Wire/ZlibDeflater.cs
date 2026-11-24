namespace Sacred.GameServer.Wire;

/// <summary>
/// Allocation-free zlib compressor: greedy LZ77 (single hash table) with the fixed Huffman code. Slightly larger than zlib -6 but
/// fast, deterministic and trivially AOT safe. Falls back to stored blocks when the data does not compress. One instance per thread.
/// </summary>
public sealed class ZlibDeflater
{
    private const int HashBits = 13, WindowSize = 32768, MinMatch = 3, MaxMatch = 258;
    private readonly int[] _head = new int[1 << HashBits];

    private static readonly (ushort Code, byte Bits)[] LitLen = BuildLitLen();
    private static readonly byte[] LengthSymbol = BuildLengthSymbols();   // index: length (3..258) -> symbol 257..285
    private static readonly short[] LenBase = [3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258];
    private static readonly byte[] LenExtra = [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0];
    private static readonly short[] DistBase = [1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577];
    private static readonly byte[] DistExtra = [0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13];

    private static ushort Reverse(int code, int bits)
    {
        var result = 0;
        for (var i = 0; i < bits; i++) { result = (result << 1) | (code & 1); code >>= 1; }
        return (ushort)result;
    }

    private static (ushort, byte)[] BuildLitLen()
    {
        var table = new (ushort, byte)[288];
        for (var i = 0; i < 288; i++)
        {
            int code, bits;
            if (i < 144) { code = 0x30 + i; bits = 8; }
            else if (i < 256) { code = 0x190 + i - 144; bits = 9; }
            else if (i < 280) { code = i - 256; bits = 7; }
            else { code = 0xC0 + i - 280; bits = 8; }
            table[i] = (Reverse(code, bits), (byte)bits);
        }
        return table;
    }

    private static byte[] BuildLengthSymbols()
    {
        var table = new byte[MaxMatch + 1];
        for (var symbol = 0; symbol < 29; symbol++)
        {
            var start = LenBaseStatic(symbol);
            var span = symbol == 28 ? 1 : 1 << LenExtraStatic(symbol);
            for (var k = 0; k < span && start + k <= MaxMatch; k++) table[start + k] = (byte)symbol;
        }
        table[258] = 28;
        return table;
    }
    private static int LenBaseStatic(int s) => new short[] { 3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258 }[s];
    private static int LenExtraStatic(int s) => new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0 }[s];

    private ref struct BitWriter
    {
        public Span<byte> Out;
        public int Pos;
        public ulong Buffer;
        public int Count;
        public bool Overflow;

        public void Write(uint value, int bits)
        {
            Buffer |= (ulong)value << Count;
            Count += bits;
            while (Count >= 8)
            {
                if (Pos >= Out.Length) { Overflow = true; return; }
                Out[Pos++] = (byte)Buffer;
                Buffer >>= 8; Count -= 8;
            }
        }

        public void Flush()
        {
            if (Count > 0) { if (Pos >= Out.Length) { Overflow = true; return; } Out[Pos++] = (byte)Buffer; Buffer = 0; Count = 0; }
        }
    }

    /// <summary>Compress <paramref name="source"/> into a zlib stream. Returns the byte count, or -1 if <paramref name="destination"/> is too small.</summary>
    public int Compress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (destination.Length < 6) return -1;
        destination[0] = 0x78; destination[1] = 0x01;
        var w = new BitWriter { Out = destination, Pos = 2 };
        Array.Clear(_head);
        w.Write(1, 1); w.Write(1, 2); // final block, fixed Huffman
        var i = 0;
        while (i < source.Length)
        {
            var bestLength = 0; var bestDistance = 0;
            if (i + MinMatch <= source.Length)
            {
                var h = Hash(source, i);
                var candidate = _head[h] - 1;
                _head[h] = i + 1;
                if (candidate >= 0 && i - candidate <= WindowSize)
                {
                    var limit = Math.Min(MaxMatch, source.Length - i);
                    var n = 0;
                    while (n < limit && source[candidate + n] == source[i + n]) n++;
                    if (n >= MinMatch) { bestLength = n; bestDistance = i - candidate; }
                }
            }
            if (bestLength >= MinMatch)
            {
                var symbol = LengthSymbol[bestLength];
                var (code, bits) = LitLen[257 + symbol];
                w.Write(code, bits);
                if (LenExtra[symbol] > 0) w.Write((uint)(bestLength - LenBase[symbol]), LenExtra[symbol]);
                var d = 0;
                while (d + 1 < 30 && DistBase[d + 1] <= bestDistance) d++;
                w.Write(Reverse(d, 5), 5);
                if (DistExtra[d] > 0) w.Write((uint)(bestDistance - DistBase[d]), DistExtra[d]);
                for (var k = 1; k < bestLength && i + k + MinMatch <= source.Length; k++) _head[Hash(source, i + k)] = i + k + 1;
                i += bestLength;
            }
            else
            {
                var (code, bits) = LitLen[source[i]];
                w.Write(code, bits);
                i++;
            }
            if (w.Overflow) return Stored(source, destination);
        }
        w.Write(LitLen[256].Code, LitLen[256].Bits);
        w.Flush();
        if (w.Overflow || w.Pos + 4 > destination.Length) return Stored(source, destination);
        WriteAdler(source, destination[w.Pos..]);
        return w.Pos + 4;
    }

    private static int Stored(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var blocks = Math.Max(1, (source.Length + 65534) / 65535);
        var total = 2 + blocks * 5 + source.Length + 4;
        if (total > destination.Length) return -1;
        destination[0] = 0x78; destination[1] = 0x01;
        var pos = 2; var offset = 0;
        for (var b = 0; b < blocks; b++)
        {
            var len = Math.Min(65535, source.Length - offset);
            destination[pos++] = (byte)(b == blocks - 1 ? 1 : 0);
            destination[pos++] = (byte)len; destination[pos++] = (byte)(len >> 8);
            destination[pos++] = (byte)~len; destination[pos++] = (byte)(~len >> 8);
            source.Slice(offset, len).CopyTo(destination[pos..]);
            pos += len; offset += len;
        }
        WriteAdler(source, destination[pos..]);
        return pos + 4;
    }

    private static int Hash(ReadOnlySpan<byte> s, int i) =>
        (int)(((uint)(s[i] | (s[i + 1] << 8) | (s[i + 2] << 16)) * 2654435761u) >> (32 - HashBits));

    private static void WriteAdler(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        uint a = 1, b = 0;
        var offset = 0;
        while (offset < source.Length)
        {
            var chunk = Math.Min(5552, source.Length - offset);
            for (var k = 0; k < chunk; k++) { a += source[offset + k]; b += a; }
            a %= 65521; b %= 65521; offset += chunk;
        }
        var value = (b << 16) | a;
        destination[0] = (byte)(value >> 24); destination[1] = (byte)(value >> 16); destination[2] = (byte)(value >> 8); destination[3] = (byte)value;
    }
}
