namespace ParticleEmitterDataset;

internal sealed class PakTable(string path)
{
    private readonly byte[] _bytes = File.ReadAllBytes(path);
    public uint Count => BitConverter.ToUInt32(_bytes, 4);

    public PakRow Get(uint id)
    {
        if (id >= Count) throw new ArgumentOutOfRangeException(nameof(id));
        var d = checked(0x100 + (int)id * 12);
        var type = BitConverter.ToUInt32(_bytes, d);
        var offset = BitConverter.ToUInt32(_bytes, d+4);
        var size = BitConverter.ToUInt32(_bytes, d+8);
        if (offset == 0 || size == 0) return new(id, type, offset, size, []);
        if ((ulong)offset + size > (ulong)_bytes.Length) throw new InvalidDataException("Invalid PAK record bounds");
        return new(id, type, offset, size, _bytes.AsSpan((int)offset, (int)size).ToArray());
    }
}
internal sealed record PakRow(uint Id, uint Type, uint Offset, uint Size, byte[] Bytes);
