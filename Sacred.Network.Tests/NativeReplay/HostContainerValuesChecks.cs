using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Scripting;

internal static class HostContainerValuesChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        foreach (var name in new[] { "native-host-container-values.json", "native-host-book-values.json" })
        {
        using var proof = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence", name)));
        var source = proof.RootElement;
        var chunks = source.GetProperty("definition_chunks").EnumerateArray().ToArray();
        var image = new byte[chunks.Max(row => row.GetProperty("offset").GetInt32() + Bytes(row, "hex").Length)];
        foreach (var row in chunks) Bytes(row, "hex").CopyTo(image, row.GetProperty("offset").GetInt32());
        var definitions = SacredScriptObjectValueDefinitions.Read(image);
        var data = EquipmentGenerationFixture.ReadStartup(root);
        foreach (var row in source.GetProperty("exclusion_controls").EnumerateArray())
            Check(definitions.RetainedResources.Contains(row.GetProperty("resource").GetUInt32()) == row.GetProperty("result").GetBoolean(), "native item-value exclusion predicate");
        foreach (var row in source.GetProperty("cases").EnumerateArray())
        {
            var instance = Bytes(row, "instance_hex");
            var resource = BinaryPrimitives.ReadUInt32LittleEndian(instance);
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var values = WorldScriptObjectValues.Prepare(resource, instance,
                row.GetProperty("class_calls")[0][0].GetUInt32(), data, definitions, random);
            Check(values.AsSpan().SequenceEqual(Bytes(row, "runtime_hex").AsSpan(0x118, 130)), "actual active-hero container values");
            Check(random.State == row.GetProperty("rng_after").GetUInt32(), "actual caller's two RNG draws");
            Check(instance.AsSpan().SequenceEqual(Bytes(row, "instance_hex")), "authored source remains immutable");
        }
        }
        return checks;
        void Check(bool condition, string reason) { if (!condition) throw new Exception("Host container values: " + reason); checks++; }
    }
    private static byte[] Bytes(JsonElement row, string key) => Convert.FromHexString(row.GetProperty(key).GetString()!);
}
