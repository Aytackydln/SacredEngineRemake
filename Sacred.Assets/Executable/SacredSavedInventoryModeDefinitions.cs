using System.Buffers.Binary;

namespace Sacred.Assets.Executable;

/// <summary>C2's original predicate for the separate inventory-mode action.
/// Resolves its call target from the loader, then reads the two native resource
/// operands. The host guards that unimplemented action; no resource IDs are copied.</summary>
public sealed class SacredSavedInventoryModeDefinitions
{
    private readonly byte _first;
    private readonly byte _second;
    private SacredSavedInventoryModeDefinitions(byte first, byte second) => (_first, _second) = (first, second);
    public bool RequiresTransition(uint resource) => resource == _first || resource == _second;
    public static SacredSavedInventoryModeDefinitions Read(byte[] image)
    {
        ReadOnlySpan<byte> suffix = [0x84,0xc0,0x74,0x0d,0x8b,0x4c,0x24,0x28,0x6a,1,0x53,0x53,0xe8];
        var found = -1;
        for (var at = 0; at <= image.Length - 19; at++)
        {
            if (image[at] != 0x55 || image[at + 1] != 0xe8 || !image.AsSpan(at + 6, suffix.Length).SequenceEqual(suffix)) continue;
            if (found >= 0) throw new InvalidDataException("Ambiguous C2 inventory-mode predicate caller.");
            found = at;
        }
        if (found < 0) throw new InvalidDataException("Original C2 inventory-mode predicate caller unavailable.");
        var reader = new SacredNativeTypeSwitch(image);
        var target = checked((ulong)((long)reader.Address(found + 6) + BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(found + 2))));
        var code = reader.At(target, 45).ToArray();
        var first = code[6]; var second = code[11]; code[6] = code[11] = 0;
        ReadOnlySpan<byte> expected = [0x8b,0x44,0x24,4,0x83,0xf8,0,0x74,0x13,0x83,0xf8,0,0x74,0x0e,0x33,0xc0,
            0x33,0xc9,0x85,0xc0,0x0f,0x95,0xc1,0x8a,0xc1,0xc2,4,0,0xb8,1,0,0,0,0x33,0xc9,0x85,0xc0,0x0f,0x95,0xc1,0x8a,0xc1,0xc2,4,0];
        if (!code.AsSpan().SequenceEqual(expected)) throw new InvalidDataException("Uncompared original C2 inventory-mode predicate.");
        return new(first, second);
    }
}
