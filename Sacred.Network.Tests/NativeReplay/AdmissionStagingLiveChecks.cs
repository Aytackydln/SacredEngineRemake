using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Sacred.GameplayHost.Networking;
using Sacred.GameplayHost.World;
using Sacred.Network.Gameplay;

internal static class AdmissionStagingLiveChecks
{
    public static byte[] WireExample(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-admission-staging-live.json")));
        var row = native.RootElement.GetProperty("cases").EnumerateArray().First(value => value.GetProperty("scenario").GetString() == "none" &&
            value.GetProperty("flags").GetInt32() == 9 && value.GetProperty("slot").GetInt32() == 16);
        return Convert.FromHexString(row.GetProperty("wire_hex").GetString()!);
    }

    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-admission-staging-live.json")));
        var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var template = native.RootElement.GetProperty("templates").GetProperty(row.GetProperty("profile_present").GetInt32().ToString());
            var fixture = new AdmissionLiveFixture(row, Convert.FromHexString(template.GetString()!));
            var control = fixture.Controller;
            control.WriteByte(0x28, 0); control.WriteByte(0x44, 7); control.WriteByte(0x37b8, 0x4b);
            fixture.Connection.WriteWord(0x40424, row.GetProperty("connection_flags").GetUInt32());
            var names = Enumerable.Repeat("", 17).ToArray(); var identifiers = Enumerable.Repeat("", 17).ToArray();
            var events = new List<string>(); var scenario = row.GetProperty("scenario").GetString()!;
            var other = 17u - row.GetProperty("slot").GetUInt32(); var convertingPrefix = true; var captured = false;
            var strings = new WorldRosterStringStorage(slot => names[slot], (slot, name) => names[slot] = name,
                (slot, name) => identifiers[slot] = name);
            var roster = new WorldRosterLiveServices(control, strings, name =>
            {
                if (convertingPrefix)
                {
                    convertingPrefix = false; events.Add("identifier");
                    if (scenario == "identifier-slot") control.WriteWord(0x14, other);
                    if (scenario == "identifier-flags") fixture.Connection.WriteWord(0x40424, 2);
                }
                return name;
            }, () => true, _ =>
            {
                if (captured) return; captured = true;
                var staged = row.GetProperty("staged");
                Check(Enumerable.Range(0, 0x50).Select(at => control.ReadByte((uint)at)).SequenceEqual(Convert.FromHexString(staged.GetProperty("header").GetString()!)), "submitted header");
                Check(Hash(fixture.Roster) == staged.GetProperty("roster").GetString(), "submitted roster");
                Check(names.Skip(1).Select(RosterLiveFixture.Hex).SequenceEqual(staged.GetProperty("names").EnumerateArray().Select(value => value.GetString()!)), "prefix name destinations");
                Check(identifiers.Skip(1).Select(RosterLiveFixture.Hex).SequenceEqual(staged.GetProperty("identifiers").EnumerateArray().Select(value => value.GetString()!)), "prefix identifier destinations");
            }, _ => { }, () => { }, () => new(_ => "K"), () => { }, _ => { });
            var staging = new WorldAdmissionStagingServices(roster, () => fixture.Connection,
                () => Encoding.Unicode.GetBytes("Hero"), () => Encoding.Unicode.GetBytes("Saved"), () => 0x76543210,
                () =>
                {
                    events.Add("alternate");
                    if (scenario == "alternate-slot") control.WriteWord(0x14, other);
                    return 0x22222222;
                }, stage =>
                {
                    events.Add(stage);
                    if (scenario == stage + "-slot") control.WriteWord(0x14, other);
                    if (scenario == "accepted-fields" && stage == "accepted")
                    { control.WriteByte(0x44, 9); control.WriteByte(0x37b8, 0x6a); }
                });
            var owner = new SacredGameplayReplicaSession();
            owner.Apply(GoldGameEventDecoder.Decode(new(0x11, 0, 0, 0, Convert.FromHexString(row.GetProperty("wire_hex").GetString()!))));
            var receipt = owner.Admission.Current!;
            owner.Admission.DeliverLive(receipt, fixture.Services with { AcceptedRoster = _ => owner.Admission.DeliverLiveStaging(receipt, staging) });
            Check(events.SequenceEqual(row.GetProperty("stage_events").EnumerateArray().Select(value => value.GetString()!)), "service order");
            Check(Hash(fixture.Roster) == row.GetProperty("roster_sha256").GetString(), "final roster after actual live consumer");
            Check(fixture.Header.SequenceEqual(Convert.FromHexString(row.GetProperty("controller_prefix").GetString()!)) &&
                fixture.Connection.ReadWord(0x48) == row.GetProperty("connection_local").GetUInt32(), "final controller/connection");
            Check(captured && receipt.LiveStagingComplete && receipt.LiveRosterComplete && receipt.DeliveryComplete && owner.Replicas.Objects.Count == 0, "composed receipt services without actor readiness");

            void Check(bool pass, string why) { if (!pass) throw new Exception($"Live staging {scenario} flags={row.GetProperty("flags")} slot={row.GetProperty("slot")}: {why}"); checks++; }
        }
        return checks;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
