using System.Buffers.Binary;
namespace Sacred.Assets.Executable;
/// <summary>Original SetOwner dialog selectors and object flag at 0x4626F3..0x46271A.</summary>
public sealed record SacredHorseOwnerDefinitions(uint DialogFlags, uint NormalDialog, uint SpecialDialog)
{
    public static SacredHorseOwnerDefinitions Read(ReadOnlySpan<byte> image)
    {
        ReadOnlySpan<byte> anchor = [0x8B,0x4D,0x14,0x8B,0x86,0xF4,1,0,0,0x81,0xC9];
        var at = image.IndexOf(anchor);
        if (at < 0 || image[(at+1)..].IndexOf(anchor)>=0) throw new InvalidDataException("Unique original Horse owner dialog code unavailable.");
        var body = image.Slice(at,39);
        return new(BinaryPrimitives.ReadUInt32LittleEndian(body[11..]), BinaryPrimitives.ReadUInt32LittleEndian(body[33..]), BinaryPrimitives.ReadUInt32LittleEndian(body[26..]));
    }
    public static SacredHorseOwnerDefinitions Load(string directory)
    {
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(directory,name); if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); } catch (InvalidDataException) { }
        }
        throw new InvalidDataException("Original Horse owner dialog definitions unavailable.");
    }
}
