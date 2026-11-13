using System.Collections.Frozen;
using System.Text.Json;
using Sacred.Core.GameRes;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class StartupCreatureResourceChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-startup-creature-resources.json")));
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        foreach (var row in fixture.RootElement.GetProperty("names").EnumerateArray())
        {
            var strings = new Dictionary<uint, string>();
            var text = row.GetProperty("text");
            if (text.ValueKind != JsonValueKind.Null)
                strings[SacredResourceHash.Compute(row.GetProperty("resource").GetUInt32().ToString(System.Globalization.CultureInfo.InvariantCulture))] = text.GetString()!;
            var resources = new GoldScriptResourceReplica();
            if (row.GetProperty("final_resource").ValueKind == JsonValueKind.Null)
            {
                try { WorldScriptCreatureResourceNames.Resolve(row.GetProperty("name").GetString(), resources, new(strings.ToFrozenDictionary())); throw new Exception("Missing local reservation accepted."); }
                catch (InvalidDataException) { checks++; }
                continue;
            }
            Check(WorldScriptCreatureResourceNames.Resolve(row.GetProperty("name").GetString(), resources,
                new(strings.ToFrozenDictionary())) == row.GetProperty("final_resource").GetUInt32(), "Native numeric name decode and NON_UNIQUE gate.");
        }
        foreach (var row in fixture.RootElement.GetProperty("gates").EnumerateArray())
        {
            var resources = new GoldScriptResourceReplica();
            if (row.GetProperty("mapped").ValueKind != JsonValueKind.Null) resources.BindObject(8300, row.GetProperty("mapped").GetUInt32());
            var objects = row.GetProperty("objects").EnumerateArray().ToDictionary(value => value.GetProperty("reference").GetUInt32(), value =>
                new GoldReplicatedObject(new(value.GetProperty("reference").GetUInt32(), value.GetProperty("type").GetUInt32(),
                    value.GetProperty("flags").GetUInt32(), 0, 0, 0, 0, 0, 0, 0, 1, value.GetProperty("resource").GetUInt32()), null, null));
            uint? Find(uint id) => resources.FindObject(id, objects, _ => 8301);
            var resource = row.GetProperty("resource").GetUInt32();
            var expected = row.GetProperty("existing");
            Check((resource is 0 or uint.MaxValue ? null : Find(resource)) == (expected.ValueKind == JsonValueKind.Null ? null : expected.GetUInt32()), "Native preallocation lookup includes inactive and template resource identities.");
            var before = new GoldObjectBaseState(26, 500, 0xa5a5a5a5, 0xa5a5, -1, -2, 0xa5, 0xa5a5a5a5, 0xa5a5a5a5, 0xa5, 1, 0xa5a5a5a5);
            var revision = resources.Revision;
            var after = WorldScriptCreatureResourceNames.Bind(before, resource, resources, Find);
            Check(after.Word3C == row.GetProperty("instance_resource").GetUInt32() && after with { Word3C = before.Word3C } == before, "Native setter changes only instance resource.");
            var inserted = row.GetProperty("inserted").GetArrayLength() != 0;
            Check(resources.Revision == revision + (inserted ? 1 : 0) && (!inserted || resources.ObjectBindings[resource] == 26), "Native conditional map insertion, including existing self binding.");
        }
        var names = new GoldScriptResourceReplica();
        Check(WorldScriptCreatureResourceNames.Resolve(null, names, null) == uint.MaxValue &&
            WorldScriptCreatureResourceNames.Resolve("hero", names, null) == uint.MaxValue &&
            WorldScriptCreatureResourceNames.Resolve("non_unique", names, null) == uint.MaxValue, "Native unnamed special operands.");
        foreach (var name in new[] { "Res:missing", "Res:17085tail", "Res:-1", "$name", "new-local" })
        {
            try { WorldScriptCreatureResourceNames.Resolve(name, names, GameResStore.Empty); throw new Exception("Uncompared name accepted."); }
            catch (InvalidDataException) { checks++; }
        }
        return checks;
    }
}
