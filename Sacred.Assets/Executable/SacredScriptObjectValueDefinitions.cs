using System.Buffers.Binary;

namespace Sacred.Assets.Executable;

/// <summary>Original 43AF50 item-base exclusion table used by CreateObj before
/// active-hero normalization/generation. Resource IDs remain executable data.</summary>
public sealed record SacredScriptObjectValueDefinitions(IReadOnlySet<uint> RetainedResources)
{
    public static SacredScriptObjectValueDefinitions Read(byte[] image)
    {
        ReadOnlySpan<byte> comparison = [0x39,0x10,0x74,0x10,0x83,0xc0,4,0x3d];
        var at = image.AsSpan().IndexOf(comparison);
        if (at < 5 || image[at-5] != 0xb8 || image.AsSpan(at+1).IndexOf(comparison) >= 0 ||
            !image.AsSpan(at+12).StartsWith<byte>([0x72,0xf2,0x32,0xc0,0x5e,0xc2,4,0,0xb0,1]))
            throw new InvalidDataException("Original CreateObj item-value exclusion instructions unavailable.");
        var begin = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(at-4));
        var end = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(at+8));
        if (end <= begin || (end-begin)%4 != 0 || end-begin > 128)
            throw new InvalidDataException("Invalid original CreateObj item-value exclusion table.");
        var table = new SacredNativeTypeSwitch(image).At(begin, checked((int)(end-begin)));
        var resources = new HashSet<uint>();
        for (var offset = 0; offset < table.Length; offset += 4)
            resources.Add(BinaryPrimitives.ReadUInt32LittleEndian(table[offset..]));
        return new(resources);
    }

    public static SacredScriptObjectValueDefinitions Load(string installation)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(installation, name);
            if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); }
            catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original CreateObj item-value definitions unavailable.", failure);
    }
}
