namespace Sacred.GameServer.Wire;

/// <summary>
/// Allocation-free zlib inflater (port of the public-domain "puff" algorithm). One instance per thread;
/// all Huffman tables are preallocated. Used instead of ZLibStream, which allocates per call.
/// </summary>
public sealed class ZlibInflater
{
    private const int MaxBits = 15, MaxLCodes = 286, MaxDCodes = 30, FixLCodes = 288;

    private readonly short[] _lenCount = new short[MaxBits + 1], _lenSym = new short[FixLCodes];
    private readonly short[] _distCount = new short[MaxBits + 1], _distSym = new short[MaxDCodes];
    private readonly short[] _fixLenCount = new short[MaxBits + 1], _fixLenSym = new short[FixLCodes];
    private readonly short[] _fixDistCount = new short[MaxBits + 1], _fixDistSym = new short[MaxDCodes];
    private readonly short[] _lengths = new short[MaxLCodes + MaxDCodes + 19];

    private static readonly short[] LenBase = [3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258];
    private static readonly short[] LenExtra = [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0];
    private static readonly short[] DistBase = [1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577];
    private static readonly short[] DistExtra = [0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13];
    private static readonly byte[] CodeOrder = [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];

    public ZlibInflater()
    {
        var lengths = new short[FixLCodes];
        var symbol = 0;
        for (; symbol < 144; symbol++) lengths[symbol] = 8;
        for (; symbol < 256; symbol++) lengths[symbol] = 9;
        for (; symbol < 280; symbol++) lengths[symbol] = 7;
        for (; symbol < FixLCodes; symbol++) lengths[symbol] = 8;
        Construct(_fixLenCount, _fixLenSym, lengths, FixLCodes);
        for (symbol = 0; symbol < MaxDCodes; symbol++) lengths[symbol] = 5;
        Construct(_fixDistCount, _fixDistSym, lengths, MaxDCodes);
    }

    private ref struct State
    {
        public ReadOnlySpan<byte> In;
        public int InPos;
        public Span<byte> Out;
        public int OutPos;
        public uint BitBuf;
        public int BitCount;
        public bool Failed;
    }

    /// <summary>Inflate a zlib stream (2-byte header, deflate data, adler trailer ignored) into <paramref name="output"/>.</summary>
    public bool TryInflate(ReadOnlySpan<byte> zlib, Span<byte> output, out int written)
    {
        written = 0;
        if (zlib.Length < 2 || (zlib[0] & 0x0F) != 8 || ((zlib[0] << 8) | zlib[1]) % 31 != 0 || (zlib[1] & 0x20) != 0) return false;
        var s = new State { In = zlib, InPos = 2, Out = output };
        int last;
        do
        {
            last = Bits(ref s, 1);
            var type = Bits(ref s, 2);
            if (s.Failed) return false;
            var ok = type switch { 0 => Stored(ref s), 1 => Codes(ref s, _fixLenCount, _fixLenSym, _fixDistCount, _fixDistSym), 2 => Dynamic(ref s), _ => false };
            if (!ok || s.Failed) return false;
        } while (last == 0);
        written = s.OutPos;
        return true;
    }

    private static int Bits(ref State s, int need)
    {
        var value = s.BitBuf;
        while (s.BitCount < need)
        {
            if (s.InPos >= s.In.Length) { s.Failed = true; return 0; }
            value |= (uint)s.In[s.InPos++] << s.BitCount;
            s.BitCount += 8;
        }
        s.BitBuf = value >> need;
        s.BitCount -= need;
        return (int)(value & ((1u << need) - 1));
    }

    private static bool Stored(ref State s)
    {
        s.BitBuf = 0; s.BitCount = 0;
        if (s.InPos + 4 > s.In.Length) return false;
        int len = s.In[s.InPos] | (s.In[s.InPos + 1] << 8);
        int nlen = s.In[s.InPos + 2] | (s.In[s.InPos + 3] << 8);
        s.InPos += 4;
        if (len != (~nlen & 0xFFFF) || s.InPos + len > s.In.Length || s.OutPos + len > s.Out.Length) return false;
        s.In.Slice(s.InPos, len).CopyTo(s.Out[s.OutPos..]);
        s.InPos += len; s.OutPos += len;
        return true;
    }

    private static int Decode(ref State s, short[] count, short[] symbol)
    {
        int code = 0, first = 0, index = 0;
        for (var len = 1; len <= MaxBits; len++)
        {
            code |= Bits(ref s, 1);
            if (s.Failed) return -1;
            int c = count[len];
            if (code - c < first) return symbol[index + (code - first)];
            index += c; first += c; first <<= 1; code <<= 1;
        }
        return -1;
    }

    private static int Construct(short[] count, short[] symbol, ReadOnlySpan<short> length, int n)
    {
        for (var len = 0; len <= MaxBits; len++) count[len] = 0;
        for (var sym = 0; sym < n; sym++) count[length[sym]]++;
        if (count[0] == n) return 0;
        var left = 1;
        for (var len = 1; len <= MaxBits; len++) { left <<= 1; left -= count[len]; if (left < 0) return left; }
        Span<short> offs = stackalloc short[MaxBits + 1];
        for (var len = 1; len < MaxBits; len++) offs[len + 1] = (short)(offs[len] + count[len]);
        for (var sym = 0; sym < n; sym++) if (length[sym] != 0) symbol[offs[length[sym]]++] = (short)sym;
        return left;
    }

    private static bool Codes(ref State s, short[] lc, short[] ls, short[] dc, short[] ds)
    {
        while (true)
        {
            var symbol = Decode(ref s, lc, ls);
            if (symbol < 0) return false;
            if (symbol < 256)
            {
                if (s.OutPos >= s.Out.Length) return false;
                s.Out[s.OutPos++] = (byte)symbol;
            }
            else if (symbol == 256) return true;
            else
            {
                symbol -= 257;
                if (symbol >= 29) return false;
                var len = LenBase[symbol] + Bits(ref s, LenExtra[symbol]);
                var d = Decode(ref s, dc, ds);
                if (d < 0 || d >= MaxDCodes) return false;
                var dist = DistBase[d] + Bits(ref s, DistExtra[d]);
                if (s.Failed || dist > s.OutPos || s.OutPos + len > s.Out.Length) return false;
                for (var i = 0; i < len; i++) { s.Out[s.OutPos] = s.Out[s.OutPos - dist]; s.OutPos++; }
            }
        }
    }

    private bool Dynamic(ref State s)
    {
        var nlen = Bits(ref s, 5) + 257;
        var ndist = Bits(ref s, 5) + 1;
        var ncode = Bits(ref s, 4) + 4;
        if (s.Failed || nlen > MaxLCodes || ndist > MaxDCodes) return false;
        var lengths = _lengths;
        var index = 0;
        for (; index < ncode; index++) lengths[CodeOrder[index]] = (short)Bits(ref s, 3);
        for (; index < 19; index++) lengths[CodeOrder[index]] = 0;
        if (Construct(_lenCount, _lenSym, lengths, 19) != 0) return false;
        index = 0;
        while (index < nlen + ndist)
        {
            var symbol = Decode(ref s, _lenCount, _lenSym);
            if (symbol < 0) return false;
            if (symbol < 16) lengths[index++] = (short)symbol;
            else
            {
                short len = 0; int repeat;
                if (symbol == 16) { if (index == 0) return false; len = lengths[index - 1]; repeat = 3 + Bits(ref s, 2); }
                else if (symbol == 17) repeat = 3 + Bits(ref s, 3);
                else repeat = 11 + Bits(ref s, 7);
                if (s.Failed || index + repeat > nlen + ndist) return false;
                while (repeat-- > 0) lengths[index++] = len;
            }
        }
        if (lengths[256] == 0) return false;
        var err = Construct(_lenCount, _lenSym, lengths, nlen);
        if (err < 0 || (err > 0 && nlen - _lenCount[0] != 1)) return false;
        err = Construct(_distCount, _distSym, lengths.AsSpan(nlen), ndist);
        if (err < 0 || (err > 0 && ndist - _distCount[0] != 1)) return false;
        return Codes(ref s, _lenCount, _lenSym, _distCount, _distSym);
    }
}
