using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Runtime;

internal static class EquipmentLiveSelectionChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-live-selection.json")));
        var json = fixture.RootElement;
        var preferences = SacredEquipmentPreferenceDefinitions.Read(Image(json));
        var checks = 0;
        foreach (var variant in json.GetProperty("definition_variants").EnumerateArray())
        {
            var actual = SacredEquipmentPreferenceDefinitions.Read(Image(variant));
            if (actual.ResourceMasks.Count != preferences.ResourceMasks.Count || actual.ResourceMasks.Any(pair =>
                !preferences.ResourceMasks.TryGetValue(pair.Key, out var mask) || mask != pair.Value))
                throw new Exception("Installed native preference aliases differ.");
            checks += actual.ResourceMasks.Count + 1;
        }
        var candidates = json.GetProperty("candidates").EnumerateArray().Select(row => new SacredEquipmentSelectionCandidate(
            row.GetProperty("item_type").GetUInt32(), 0, 0,
            MemoryMarshal.Read<SacredEquipmentInstanceLayout>(Convert.FromHexString(row.GetProperty("values_hex").GetString()!)), null)).ToArray();
        foreach (var row in json.GetProperty("cases").EnumerateArray())
        {
            var inputs = row.GetProperty("inputs");
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var events = row.GetProperty("events");
            var index = 0;
            var attempt = 0;
            var mode = inputs.GetProperty("mode").GetString();
            var actual = SacredEquipmentSelectionDraw.Choose(candidates.Take(row.GetProperty("count").GetInt32()).ToArray(),
                row.GetProperty("selector").GetUInt32(), random, inputs.GetProperty("hero").GetBoolean(), new(
                    resource =>
                    {
                        if (!Effect("Creation", resource)) return null;
                        var created = mode == "no-create" || mode == "mixed" && attempt == 0 ? 0U : (uint)(500 + attempt);
                        attempt++;
                        return created;
                    },
                    reference => Effect("Fit", reference) ? mode == "accept" || mode == "mixed" && attempt >= 3 : null,
                    reference => Effect("Destruction", reference)), preferences);
            var expectedResource = row.GetProperty("selected");
            if (actual.Resource != (expectedResource.ValueKind == JsonValueKind.Null ? null : expectedResource.GetUInt32()) ||
                actual.Pending.ToString() != row.GetProperty("pending").GetString() || actual.TemporaryReference != row.GetProperty("temporary").GetUInt32() ||
                index != events.GetArrayLength() || random.State != row.GetProperty("final_seed").GetUInt32())
                throw new Exception($"Native live selection differs: {row}; actual {actual}, events {index}, RNG {random.State}");
            checks += 5;

            bool Effect(string kind, uint argument)
            {
                if (index >= events.GetArrayLength()) throw new Exception($"Unexpected live selection {kind}: {row}");
                var expected = events[index++];
                if (kind != expected.GetProperty("kind").GetString() || random.State != expected.GetProperty("seed").GetUInt32() ||
                    argument != expected.GetProperty("arguments")[0].GetUInt32())
                    throw new Exception($"Native live selection effect differs: {row}; actual {kind}({argument}), RNG {random.State}");
                checks += 3;
                if (kind == inputs.GetProperty("unresolved").GetString()) return false;
                for (var draw = 0; draw < inputs.GetProperty("draws").GetInt32(); draw++) random.Next();
                return true;
            }
        }
        return checks;
    }

    private static byte[] Image(JsonElement source)
    {
        var image = new byte[source.GetProperty("image_size").GetInt32()];
        foreach (var chunk in source.GetProperty("definition_chunks").EnumerateArray())
            Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
        return image;
    }
}
