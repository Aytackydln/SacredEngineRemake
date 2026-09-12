using System.Text.Json;

namespace ParticleResearch;

internal static class TypeCatalogue
{
    public static Dictionary<uint, string> ReadAndVerify(string sampleDirectory, byte[] executable)
    {
        var result = new Dictionary<uint, string>();
        foreach (var line in File.ReadLines(Path.Combine(sampleDirectory, "Sacred.exe.type-names.jsonl")))
        {
            using var json = JsonDocument.Parse(line);
            var row = json.RootElement;
            var offset = row.GetProperty("file_offset").GetInt32();
            var expected = Convert.FromHexString(row.GetProperty("raw_slot_hex").GetString()!);
            if (offset < 0 || offset > executable.Length - expected.Length ||
                !executable.AsSpan(offset, expected.Length).SequenceEqual(expected))
                throw new InvalidDataException($"Type catalogue differs from this executable at 0x{offset:X}. Use matching research inputs.");
            result.Add(row.GetProperty("item_id").GetUInt32(), row.GetProperty("type_name").GetString()!);
        }
        return result;
    }
}
