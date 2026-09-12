using System.Buffers.Binary;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Evaluation;

internal sealed class PresetMemory(SacredExecutableImage image, int objectSize, SacredParticleQuality quality)
{
    private readonly byte[] _scratch = new byte[0x20000];
    private readonly bool[] _written = new bool[objectSize];
    public uint BaseAddress { get; } = (image.EndAddress + 0xFFFF) & ~0xFFFFu;
    public bool ReadUninitializedObject { get; private set; }

    public ReadOnlySpan<byte> Read(uint address, int size)
    {
        if (address < BaseAddress) return image.Slice(address, size);
        var offset = checked((int)(address - BaseAddress));
        if (size < 0 || offset > _scratch.Length - size)
            throw new InvalidDataException($"Preset read outside scratch memory at 0x{address:X8}.");
        for (var i = offset; i < Math.Min(offset + size, _written.Length); i++)
            if (!_written[i]) ReadUninitializedObject = true;
        return _scratch.AsSpan(offset, size);
    }

    public ulong Integer(uint address, int size)
    {
        if (address == SacredGoldExecutableProfile.ParticleQualityAddress && size == 1) return (byte)quality;
        var bytes = Read(address, size);
        return size switch
        {
            1 => bytes[0], 2 => BinaryPrimitives.ReadUInt16LittleEndian(bytes),
            4 => BinaryPrimitives.ReadUInt32LittleEndian(bytes), 8 => BinaryPrimitives.ReadUInt64LittleEndian(bytes),
            _ => throw new NotSupportedException($"Unsupported native operand width {size}.")
        };
    }

    public void Write(uint address, int size, ulong value)
    {
        if (address < BaseAddress) throw new NotSupportedException($"Initializer writes outside its scratch state at 0x{address:X8}.");
        var offset = checked((int)(address - BaseAddress));
        if (size < 0 || offset > _scratch.Length - size)
            throw new InvalidDataException($"Preset write outside scratch memory at 0x{address:X8}.");
        var bytes = _scratch.AsSpan(offset, size);
        switch (size)
        {
            case 1: bytes[0] = (byte)value; break;
            case 2: BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)value); break;
            case 4: BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)value); break;
            case 8: BinaryPrimitives.WriteUInt64LittleEndian(bytes, value); break;
            default: throw new NotSupportedException($"Unsupported native write width {size}.");
        }
        for (var i = offset; i < Math.Min(offset + size, _written.Length); i++) _written[i] = true;
    }

    public bool IsWritten(int offset, int size) => _written.AsSpan(offset, size).IndexOf(false) < 0;
    public ReadOnlySpan<byte> ObjectBytes(int offset, int size) => _scratch.AsSpan(offset, size);
}
