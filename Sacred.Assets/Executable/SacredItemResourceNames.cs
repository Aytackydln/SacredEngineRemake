using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Text;
using Sacred.Core.Executable;

namespace Sacred.Assets.Executable;

/// <summary>The static symbolic item names used by Gold 0x43CED0. A missing key
/// remains missing here; the native model-name/Bad Item fallback belongs to the
/// caller's runtime descriptor context.</summary>
public sealed class SacredItemResourceNames
{
    private SacredItemResourceNames(Dictionary<uint, string> entries) => Entries = entries.ToFrozenDictionary();
    public IReadOnlyDictionary<uint, string> Entries { get; }
    public string? Find(uint type) => Entries.GetValueOrDefault(type);

    public static SacredItemResourceNames Read(ReadOnlySpan<byte> image)
    {
        ReadOnlySpan<byte> anchor = "\0\0\0\0TYPE_INVALID\0"u8;
        var start = image.IndexOf(anchor);
        if (start < 0 || image[(start + anchor.Length)..].IndexOf(anchor) >= 0)
            throw new InvalidDataException("Unique original item resource-name table unavailable.");
        var names = new Dictionary<uint, string>();
        for (var offset = start; offset <= image.Length - SacredExecutableTypeNameLayout.SerializedSize; offset += SacredExecutableTypeNameLayout.SerializedSize)
        {
            var record = image.Slice(offset, SacredExecutableTypeNameLayout.SerializedSize);
            var bytes = record.Slice(SacredExecutableTypeNameLayout.NameOffset, SacredExecutableTypeNameLayout.NameLength);
            var end = bytes.IndexOf((byte)0);
            if (end < 0 || !bytes.StartsWith("TYPE_"u8))
                throw new InvalidDataException("Invalid original item resource-name record.");
            var name = Encoding.Latin1.GetString(bytes[..end]);
            // The original map assignment replaces an earlier value for the same key.
            names[BinaryPrimitives.ReadUInt32LittleEndian(record)] = name;
            if (name == "TYPE_LAST_ITEM") return new(names);
        }
        throw new InvalidDataException("Truncated original item resource-name table.");
    }

    public static SacredItemResourceNames Load(string installation)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(installation, name);
            if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); }
            catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original item resource names unavailable.", failure);
    }
}
