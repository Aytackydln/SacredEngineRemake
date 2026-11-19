using System.Buffers.Binary;

namespace Sacred.Assets.Executable;

/// <summary>Original fresh clock/UI constructor inputs used by the hero writer
/// before StartCode can change time. No item/resource IDs are special cased.</summary>
public sealed record SacredHostPublicationDefaults(ushort Hour, ushort Minute, ushort DayStart, ushort DayEnd)
{
    public byte HeroMode => Mode(Hour, Minute, false);
    public byte Mode(ushort hour, ushort minute, bool forced) => forced ? (byte)2 :
        hour * 60 + minute < DayStart || hour * 60 + minute > DayEnd ? (byte)1 : (byte)0;

    public static SacredHostPublicationDefaults Read(ReadOnlySpan<byte> image)
    {
        ReadOnlySpan<byte> clock = [0x89,0x7e,0x34,0xb8,0,0,0x80,0x3f,0x89,0x7e,0x30,0x66,0xc7,0x46,0x20];
        var time = Find(image, clock);
        if (time < 64 || !image.Slice(time - 64, 2).SequenceEqual<byte>([0x33,0xff]) ||
            !image.Slice(time + clock.Length + 2, 11).SequenceEqual<byte>([0x66,0x89,0x7e,0x22,0x66,0x89,0x7e,0x24,0x89,0x7e,0x28]))
            throw new InvalidDataException("Uncompared original fresh clock initialization.");
        ReadOnlySpan<byte> ui = [0x89,0x9d,0x68,0x2e,1,0,0x66,0xc7,0x85,0x4a,0x2e,1,0];
        var policy = Find(image, ui);
        if (policy < 17 || !image.Slice(policy - 17, 8).SequenceEqual<byte>([0x33,0xdb,0x56,0x57,0xc6,0x45,0,1]) ||
            !image.Slice(policy + 33, 7).SequenceEqual<byte>([0x66,0xc7,0x85,0x50,0x2e,1,0]) ||
            !image.Slice(policy + 284, 6).SequenceEqual<byte>([0x88,0x9d,0x0c,0x9e,0,0]))
            throw new InvalidDataException("Uncompared original hero day interval/override initialization.");
        return new(BinaryPrimitives.ReadUInt16LittleEndian(image[(time + clock.Length)..]), 0,
            BinaryPrimitives.ReadUInt16LittleEndian(image[(policy + 13)..]),
            BinaryPrimitives.ReadUInt16LittleEndian(image[(policy + 40)..]));
    }

    private static int Find(ReadOnlySpan<byte> image, ReadOnlySpan<byte> pattern)
    {
        var at = image.IndexOf(pattern);
        if (at < 0 || image[(at + pattern.Length)..].IndexOf(pattern) >= 0)
            throw new InvalidDataException("Original publication constructor inputs unavailable or ambiguous.");
        return at;
    }
}
