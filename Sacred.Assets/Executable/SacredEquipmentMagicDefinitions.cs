namespace Sacred.Assets.Executable;

/// <summary>Original 0x43AFC0 resource predicate used by magic generation.
/// The switch is read from installed instructions, without item-ID exceptions.</summary>
public static class SacredEquipmentMagicDefinitions
{
    public static Func<uint, bool> Read(byte[] image)
    {
        var found = -1;
        for (var at = 0; at <= image.Length - 32; at++)
        {
            var code = image.AsSpan(at);
            if (!code.StartsWith<byte>([0x8B, 0x44, 0x24, 4, 0x05]) ||
                !code[9..].StartsWith<byte>([0x83, 0xF8]) ||
                !code[12..].StartsWith<byte>([0x77, 0x14, 0x33, 0xC9, 0x8A, 0x88])) continue;
            if (found >= 0) throw new InvalidDataException("Ambiguous native magic predicate.");
            found = at;
        }
        if (found < 0) throw new InvalidDataException("Native magic predicate unavailable.");
        var native = new SacredNativeTypeSwitch(image);
        var address = native.Address(found);
        return type => (native.Read(address, type) & 255) != 0;
    }
}
