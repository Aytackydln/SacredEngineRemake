using System.Security.Cryptography;
using System.Text.Json;
using Sacred.GameplayHost.World;
using Sacred.Network.Gameplay;

internal static class AdmissionLiveChecks
{
    public static byte[] WireExample(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-admission-live.json")));
        var row = native.RootElement.GetProperty("cases").EnumerateArray().First(value =>
            value.GetProperty("scenario").GetString() == "none" && value.GetProperty("flags").GetByte() == 6 &&
            value.GetProperty("slot").GetByte() == 16 && value.GetProperty("token").GetUInt32() == 0xdeadbeef);
        return Convert.FromHexString(row.GetProperty("wire_hex").GetString()!);
    }

    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-admission-live.json")));
        var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var template = native.RootElement.GetProperty("templates").GetProperty(row.GetProperty("profile_present").GetInt32().ToString());
            var fixture = new AdmissionLiveFixture(row, Convert.FromHexString(template.GetString()!));
            var owner = new SacredGameplayReplicaSession();
            owner.Apply(GoldGameEventDecoder.Decode(new(0x11, 0, 0, 0, Convert.FromHexString(row.GetProperty("wire_hex").GetString()!))));
            var receipt = owner.Admission.Current!;
            Check(!receipt.DeliveryComplete && receipt.Plan is null && !receipt.LiveStarted, "receiving does not execute admission");
            var result = owner.Admission.DeliverLive(receipt, fixture.Services);
            Check(fixture.Header.SequenceEqual(Convert.FromHexString(row.GetProperty("controller_prefix").GetString()!)), "current controller fields");
            Check(fixture.Connection.ReadWord(0x48) == row.GetProperty("connection_local").GetUInt32(), "current connection reference");
            Check(fixture.Controller.ReadByte(0x48) == row.GetProperty("flag48").GetByte() &&
                fixture.Controller.ReadByte(0x3819) == row.GetProperty("flag3819").GetByte(), "roster staging flags");
            Check(Convert.ToHexString(SHA256.HashData(fixture.Roster)).Equals(row.GetProperty("roster_sha256").GetString(), StringComparison.OrdinalIgnoreCase), "roster after supplied service returns");
            var expectedBoundaries = row.GetProperty("boundaries").EnumerateArray().Select(value => (value[0].GetString()!, value[1].GetString()!));
            Check(fixture.Boundaries.SequenceEqual(expectedBoundaries), "field reads at synchronous service boundaries");
            Check(fixture.Queries.SequenceEqual(row.GetProperty("query_calls").EnumerateArray().Select(value => value.GetBoolean())), "query order/count");
            Check(fixture.Effects.SequenceEqual(row.GetProperty("effects").EnumerateArray().Select(value => value.GetString()!)), "outer service order");
            Check(fixture.Chats.SequenceEqual(row.GetProperty("notices").EnumerateArray().Select(value => value.GetString()!)), "chat order/flags precedence");
            var requests = row.GetProperty("requests").EnumerateArray().ToArray();
            Check(fixture.Ui.Count == requests.Length, "UI count");
            for (var i = 0; i < requests.Length; i++)
            {
                var actual = fixture.Ui[i]; var expected = requests[i];
                Check(actual.EventId == expected.GetProperty("event").GetUInt32() && actual.Target == expected.GetProperty("target").GetString() &&
                    actual.Mode == expected.GetProperty("mode").GetUInt32() && actual.Action == expected.GetProperty("action").GetUInt32() &&
                    actual.ResourceKey == expected.GetProperty("resource").GetString() && actual.Parameter == expected.GetProperty("parameter").GetUInt32(), "UI operands");
            }
            var rejected = row.GetProperty("sent").GetArrayLength() != 0;
            var roster = fixture.Effects.Contains("roster");
            Check(result.PrefixRejected == (rejected && !roster) && result.LateRejected == (rejected && roster), "rejection stage");
            Check(result.Identity.LocalReference == fixture.Controller.ReadWord(0x14) && result.Identity.HostedServerWindow == fixture.Controller.ReadWord(0x24) &&
                result.Identity.ConnectionLocalReference == fixture.Connection.ReadWord(0x48) && result.HostByte1 == fixture.Controller.ReadByte(1) &&
                result.PendingByte3 == fixture.Controller.ReadByte(3), "post-service snapshot");
            Check(receipt.LiveStarted && !receipt.LiveInProgress && receipt.DeliveryComplete && receipt.LiveResult == result &&
                receipt.Plan is null && owner.Replicas.Objects.Count == 0, "one owner completes supplied services without actor readiness");

            void Check(bool value, string message)
            {
                if (!value) throw new Exception($"Live admission {row.GetProperty("scenario")} flags={row.GetProperty("flags")} queries={row.GetProperty("queries")}: {message}");
                checks++;
            }
        }
        return checks;
    }
}
