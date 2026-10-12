namespace Sacred.Network.Protocol;

/// <summary>Preserves the native rolling word across fragmented file/stream reads.</summary>
internal struct SacredChecksumState
{
    private uint _rolling;
    private uint _result = 0x6F60093A;
    private uint _key = 0x3EF05ADB;
    private int _wordBytes;
    private bool _hasBytes;

    public SacredChecksumState() { }

    public readonly uint Value => _hasBytes ? _result ^ _rolling : 0;

    public void Append(byte value)
    {
        _hasBytes = true;
        _rolling = unchecked((_rolling << 8) | value);
        _wordBytes = (_wordBytes + 1) & 3;
        if (_wordBytes != 0) return;
        _result ^= _key ^ _rolling;
        _key = unchecked((_key ^ _rolling) + 0x63FE53BA);
    }

    public void Append(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes) Append(value);
    }
}
