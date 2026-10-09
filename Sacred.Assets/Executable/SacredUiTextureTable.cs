using System.Runtime.InteropServices;
using System.Text;
using Sacred.Core.UI;

namespace Sacred.Assets.Executable;

/// <summary>Reads the original Windows sUITextureDef array by content, without build-specific addresses.</summary>
public static class SacredUiTextureTable
{
    private const int Stride = 0x54;

    public static IReadOnlyList<SacredExecutableUiTextureDefinitionLayout> Read(string executablePath) =>
        Read(File.ReadAllBytes(executablePath));

    public static IReadOnlyList<SacredExecutableUiTextureDefinitionLayout> Read(ReadOnlySpan<byte> image)
    {
        List<SacredExecutableUiTextureDefinitionLayout>? result = null;
        var offset = 0;
        while (offset < image.Length)
        {
            var relative = image[offset..].IndexOf("INVALID\0"u8);
            if (relative < 0) break;
            var start = offset + relative - 8;
            offset += relative + 1;
            if (start < 0 || !TryRead(image[start..], out var records)) continue;
            if (result is not null) throw new InvalidDataException("Ambiguous native UI texture tables.");
            result = records;
        }
        return result ?? throw new InvalidDataException("No complete Windows UI texture table was found.");
    }

    public static string GetTextureName(SacredExecutableUiTextureDefinitionLayout record)
    {
        ReadOnlySpan<byte> name = record.TextureName;
        var end = name.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? name : name[..end]);
    }

    private static bool TryRead(ReadOnlySpan<byte> table,
        out List<SacredExecutableUiTextureDefinitionLayout> records)
    {
        records = [];
        for (var index = 0; index < table.Length / Stride; index++)
        {
            var record = MemoryMarshal.Read<SacredExecutableUiTextureDefinitionLayout>(table.Slice(index * Stride, Stride));
            ReadOnlySpan<byte> name = record.TextureName;
            var end = name.IndexOf((byte)0);
            if (end <= 0 || name[..end].ContainsAnyExceptInRange((byte)32, (byte)126) ||
                (index < 100 && record.Definition != index) ||
                !float.IsFinite(record.U0) || !float.IsFinite(record.V0) ||
                !float.IsFinite(record.U1) || !float.IsFinite(record.V1))
                return false;
            records.Add(record);
            if (name[..end].SequenceEqual("INVALID_MAX"u8)) return index >= 100;
        }
        return false;
    }
}
