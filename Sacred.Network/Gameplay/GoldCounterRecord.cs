using System.Buffers.Binary;
using System.Text;

namespace Sacred.Network.Gameplay;

/// <summary>Bank 1 section 7/8: target resource, remaining/initial counts and a 64-byte callback name.</summary>
public sealed class GoldCounterRecord
{
    private readonly byte[] _bytes;
    private GoldCounterRecord(byte[] bytes) => _bytes = bytes;
    public uint TargetResource => BinaryPrimitives.ReadUInt32LittleEndian(_bytes);
    public uint Remaining => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(4));
    public uint Initial => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(8));
    public ReadOnlySpan<byte> CallbackStorage => _bytes.AsSpan(GoldCounterLayout.NameOffset, GoldCounterLayout.NameSize);
    public string CallbackName
    {
        get { var end = CallbackStorage.IndexOf((byte)0); return Encoding.Latin1.GetString(end < 0 ? CallbackStorage : CallbackStorage[..end]); }
    }
    public static GoldCounterRecord FromPayload(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != GoldCounterLayout.RecordSize) throw new InvalidDataException("Gold counter record requires 76 bytes.");
        return new(bytes.ToArray());
    }
    public byte[] ToPayload() => _bytes.ToArray();
    internal GoldCounterRecord WithTarget(uint target) => WithWord(0, target);
    internal GoldCounterRecord Decrement() => WithWord(4, unchecked(Remaining - 1));
    private GoldCounterRecord WithWord(int offset, uint value)
    {
        var bytes = ToPayload(); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value); return new(bytes);
    }
}

/// <summary>Bank 1 section 9: category-dependent source/replacement words and retained third word.</summary>
public readonly record struct GoldCounterMapping(uint Source, uint Replacement, uint Retained);
