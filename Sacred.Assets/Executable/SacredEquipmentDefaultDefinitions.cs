namespace Sacred.Assets.Executable;

/// <summary>Original empty-slot resources from Gold 0x426580. These defaults are
/// actual objects created by detach, independently of presentation fallbacks.</summary>
public sealed class SacredEquipmentDefaultDefinitions
{
    private readonly SacredNativeTypeSwitch _reader;
    private readonly ulong _address;

    private SacredEquipmentDefaultDefinitions(SacredNativeTypeSwitch reader, ulong address)
        => (_reader, _address) = (reader, address);

    public uint Resource(uint actorResource, ushort slot) => _reader.Read(_address, actorResource, slot);

    public static SacredEquipmentDefaultDefinitions Read(byte[] image)
    {
        ReadOnlySpan<byte> signature = [0x8B, 0x44, 0x24, 4, 0x48, 0x83, 0xF8, 7, 0x77, 0x56, 0xFF, 0x24, 0x85];
        var start = image.AsSpan().IndexOf(signature);
        if (start < 0 || image.AsSpan(start + 1).IndexOf(signature) >= 0)
            throw new InvalidDataException("Original equipment default switch unavailable or ambiguous.");
        var reader = new SacredNativeTypeSwitch(image);
        var address = reader.Address(start);
        // Validate every reachable arm now, including the common empty return.
        for (uint type = 0; type <= 9; type++)
            for (ushort slot = 0; slot < 20; slot++) _ = reader.Read(address, type, slot);
        return new(reader, address);
    }

    public static SacredEquipmentDefaultDefinitions Load(string installation)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(installation, name);
            if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); }
            catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original equipment defaults unavailable.", failure);
    }
}
