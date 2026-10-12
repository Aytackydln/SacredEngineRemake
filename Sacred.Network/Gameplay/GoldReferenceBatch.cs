using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>Server 0x153 reply: consecutive words of reserved object references, with no count prefix.</summary>
public sealed class GoldReferenceBatch
{
    private readonly uint[] _references;

    private GoldReferenceBatch(uint[] references) => _references = references;

    public int Count => _references.Length;
    public ReadOnlySpan<uint> References => _references;

    public static GoldReferenceBatch FromPayload(ReadOnlySpan<byte> payload)
    {
        if ((payload.Length & 3) != 0 || payload.Length / 4 >= GoldReferenceCodec.ReferenceLimit)
            throw new InvalidDataException("Gold reference batch requires bounded, complete four-byte words.");
        var references = new uint[payload.Length / 4];
        for (var index = 0; index < references.Length; index++)
        {
            var value = BinaryPrimitives.ReadUInt32LittleEndian(payload[(index * 4)..]);
            // Server 0x4D6268..0x4D62AD excludes player refs and refs outside its reservation table.
            if (value is < GoldReferenceCodec.FirstObjectReference or >= GoldReferenceCodec.ReferenceLimit)
                throw new InvalidDataException("Gold allocated reference is outside the native object range.");
            references[index] = value;
        }
        return new GoldReferenceBatch(references);
    }
}
