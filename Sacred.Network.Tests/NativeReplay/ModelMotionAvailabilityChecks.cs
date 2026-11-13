using System.IO.Compression;
using System.Text.Json;
using Sacred.Assets.Paks.Models;
using Sacred.GameplayHost.Scripting;

internal static class ModelMotionAvailabilityChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-model-motion-availability.json")));
        using var packed = new MemoryStream(Convert.FromBase64String(fixture.RootElement.GetProperty("metadata_zlib_base64").GetString()!));
        using var zlib = new ZLibStream(packed, CompressionMode.Decompress);
        using var output = new MemoryStream(); zlib.CopyTo(output);
        var exists = false;
        var data = ModelsMotionAvailability.Read(output.ToArray(), _ => exists);
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            exists = row.GetProperty("loose_exists").GetBoolean();
            if (data.HasMotion(row.GetProperty("model").GetString()!, row.GetProperty("slot").GetInt32()) != row.GetProperty("result").GetBoolean())
                throw new Exception("Native motion availability differs: " + row);
            checks++;
        }
        exists = false;
        foreach (var row in fixture.RootElement.GetProperty("caller_cases").EnumerateArray())
        {
            var queries = new List<int>();
            var needsReplacement = !WorldCreatureRangedEquipment.CanRetain(row.GetProperty("usage").GetByte(), slot =>
            { queries.Add(slot); return data.HasMotion(row.GetProperty("model").GetString()!, slot); });
            if (needsReplacement != row.GetProperty("requires_replacement").GetBoolean())
                throw new Exception("Native CreateNPC motion/replacement gate differs: " + row);
            if (!queries.SequenceEqual(row.GetProperty("query_slots").EnumerateArray().Select(value => value.GetInt32())))
                throw new Exception("Native CreateNPC motion query order differs: " + row);
            checks++;
        }
        return checks;
    }
}
