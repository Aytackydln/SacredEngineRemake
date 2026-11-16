using System.Security.Cryptography;
using System.Text.Json;
using Sacred.GameplayHost.Networking;
using Sacred.GameplayHost.World;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;

internal static class RosterLiveChecks
{
    public static byte[] WireExample(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-roster-live.json")));
        return Convert.FromHexString(native.RootElement.GetProperty("tables").GetProperty("profiles").GetString()!);
    }

    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-roster-live.json")));
        var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var fixture = new RosterLiveFixture(row)
            {
                ExpectedUiFamily = native.RootElement.GetProperty("ui_getters").GetProperty("family").GetProperty("value").GetUInt32(),
                ExpectedUiEvent = native.RootElement.GetProperty("ui_getters").GetProperty("event").GetProperty("value").GetUInt32(),
                ExpectedUiCommand = native.RootElement.GetProperty("ui_command").GetUInt32(),
                ExpectedUiScope = native.RootElement.GetProperty("ui_scope").GetString()!
            };
            var table = Convert.FromHexString(native.RootElement.GetProperty("tables").GetProperty(row.GetProperty("mode").GetString()!).GetString()!);
            var incoming = GoldPlayerRoster.FromPayload(table);
            var owner = new SacredGameplayReplicaSession();
            // This caller supplies the standalone consumer's established fields;
            // it does not claim to implement accepted prefix/string/staging work.
            owner.Apply(GoldGameEventDecoder.Decode(new(0x11, 0, 0, 0, [1, 1, 0, 0, 0, 0])));
            var receipt = owner.Admission.Current!;
            var outer = new WorldAdmissionLiveServices(fixture.Controller, () => fixture.Controller,
                () => { }, () => false, _ => owner.Admission.DeliverLiveRoster(receipt, incoming, fixture.Services),
                _ => { }, () => { }, () => { }, _ => { });
            owner.Admission.DeliverLive(receipt, outer);
            Check(fixture.Events.Select(value => JsonSerializer.Serialize(value)).SequenceEqual(row.GetProperty("events").EnumerateArray().Select(value => value.GetRawText())), "ordered callback operands/current fields");
            Check(Convert.ToHexString(SHA256.HashData(fixture.Roster)).Equals(row.GetProperty("roster_sha256").GetString(), StringComparison.OrdinalIgnoreCase), "current roster bytes/profile preservation");
            Check(fixture.Names.Skip(1).Select(RosterLiveFixture.Hex).SequenceEqual(row.GetProperty("names").EnumerateArray().Select(value => value.GetString()!)), "current names after callback mutation");
            Check(fixture.Identifiers.Skip(1).Select(RosterLiveFixture.Hex).SequenceEqual(row.GetProperty("identifiers").EnumerateArray().Select(value => value.GetString()!)), "identifier snapshots versus changed names");
            Check(fixture.Controller.ReadWord(0x14) == row.GetProperty("local_after").GetUInt32() &&
                fixture.Controller.ReadByte(0x28) == row.GetProperty("flag28_after").GetByte() &&
                fixture.Multiplayer == (row.GetProperty("multiplayer_after").GetByte() != 0), "current controller/multiplayer fields");
            Check(receipt.LiveRosterStarted && receipt.LiveRosterComplete && receipt.DeliveryComplete &&
                receipt.Plan is null && owner.Replicas.Objects.Count == 0, "existing receipt completes nested service without actor readiness");

            void Check(bool pass, string message)
            {
                if (!pass) throw new Exception($"Live roster {row.GetProperty("mode")} {row.GetProperty("mutation")} mask={row.GetProperty("old_mask")} local={row.GetProperty("local")} gate={row.GetProperty("flag28")} mp={row.GetProperty("multiplayer")}: {message}");
                checks++;
            }
        }
        return checks;
    }
}
