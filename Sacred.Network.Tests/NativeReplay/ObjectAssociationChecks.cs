using System.Text.Json;
using Sacred.GameplayHost.Scripting;

internal static class ObjectAssociationChecks
{
    public static int Run(string root)
    {
        using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-object-association.json")));
        var checks = 0;
        foreach (var row in source.RootElement.GetProperty("cases").EnumerateArray())
        {
            var primary = row.GetProperty("primary_resource").GetUInt32();
            var secondary = row.GetProperty("secondary_resource").GetUInt32();
            var lookup = WorldScriptObjectAssociation.ShouldLookup(row.GetProperty("secondary").GetString(),
                primary, secondary, row.GetProperty("flags").GetUInt32());
            Check(lookup == (row.GetProperty("lookup_resources").GetArrayLength() != 0), "signed resource/text/flag lookup gate");
            var deferred = lookup && row.GetProperty("cast_inputs").EnumerateArray().Any(value => value.GetUInt32() != 0) &&
                row.GetProperty("target").GetString() == "creature";
            Check(deferred == (row.GetProperty("route").GetString() == "deferred"), "actual registry lookup and RTTI split");
            Check((deferred && WorldScriptObjectAssociation.RequiresOwnershipRequest(row.GetProperty("networking").GetBoolean(),
                90, row.GetProperty("owner").GetByte(), 1)) == (row.GetProperty("ownership_requests").GetArrayLength() != 0),
                "ownership request before action append");
            var actions = row.GetProperty("actions_hex");
            Check(actions.GetArrayLength() == (deferred ? (row.GetProperty("placement").GetBoolean() ? 2 : 1) : 0), "native append count");
            if (!deferred) continue;
            Check(row.GetProperty("cleared_binding").GetUInt32() == 0, "primary map entry cleared before queue append");
            var creation = WorldScriptDeferredObjectAction.FromPayload(Convert.FromHexString(actions[0].GetString()!));
            Check(creation.Kind == 11 && creation.ObjectType == 854 && creation.PrimaryResource == primary, "kind 11 identity and primary resource");
            Check(creation.Word(20) == 6305 && creation.Word(24) == 5154 && creation.Word(28) == 0, "retained tile coordinates");
            Check(creation.Word(8) == (row.GetProperty("placement").GetBoolean() ? unchecked((uint)-10) : uint.MaxValue) &&
                creation.Word(12) == (row.GetProperty("placement").GetBoolean() ? unchecked((uint)-20) : 0) &&
                creation.Word(16) == (row.GetProperty("placement").GetBoolean() ? 3 : 0), "world-coordinate sentinels and retained surface");
            foreach (var action in actions.EnumerateArray())
            {
                var bytes = Convert.FromHexString(action.GetString()!);
                var decoded = WorldScriptDeferredObjectAction.FromPayload(bytes);
                bytes[0] ^= 0xff;
                Check(Convert.ToHexString(decoded.ToPayload()).Equals(action.GetString(), StringComparison.OrdinalIgnoreCase), "owned lossless action bytes");
            }
            if (actions.GetArrayLength() == 2)
            {
                var placement = WorldScriptDeferredObjectAction.FromPayload(Convert.FromHexString(actions[1].GetString()!));
                Check(placement.Kind == 13 && placement.Word(4) == 10 && placement.Word(8) == 20 &&
                    placement.Word(12) == 0xff123456 && placement.Word(16) == 42, "separate kind 13 placement record");
            }
        }
        foreach (var reference in new uint[] { 0, 1, 16, 17, uint.MaxValue })
            Check(WorldScriptObjectAssociation.RequiresOwnershipRequest(true, reference, 2, 1) == (reference > 16), "reserved actor ownership gate");
        foreach (var size in new[] { 0, 67, 69 })
        {
            try { WorldScriptDeferredObjectAction.FromPayload(new byte[size]); throw new Exception("Invalid action accepted"); }
            catch (InvalidDataException) { checks++; }
        }
        return checks;
        void Check(bool pass, string reason) { if (!pass) throw new Exception("Object association: " + reason); checks++; }
    }
}
