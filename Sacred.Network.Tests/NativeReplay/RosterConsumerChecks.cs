using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sacred.GameplayHost.Networking;
using Sacred.Network.Admission;

internal static class RosterConsumerChecks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-roster-consumer.json")));
        var data = native.RootElement;
        var checks = 0;
        foreach (var row in data.GetProperty("cases").EnumerateArray())
        {
            var before = new byte[GoldPlayerRosterLayout.Size];
            var mask = row.GetProperty("old_mask").GetUInt32();
            for (var slot = 1; slot <= 16; slot++) before[slot * 834 + GoldPlayerDataLayout.PresenceByteOffset] = (byte)((mask >> (slot - 1)) & 1);
            Convert.FromHexString(row.GetProperty("staging_hex").GetString()!).CopyTo(before, GoldPlayerDataLayout.ProfileOffset);
            var input = Convert.FromHexString(data.GetProperty("tables").GetProperty(row.GetProperty("mode").GetString()!).GetString()!);
            var stage = WorldRosterApplication.Apply(GoldPlayerRoster.FromPayload(before), GoldPlayerRoster.FromPayload(input),
                row.GetProperty("local").GetByte(), row.GetProperty("flag28").GetBoolean(),row.GetProperty("multiplayer").GetBoolean(), value => value);
            Check(Convert.ToHexString(SHA256.HashData(stage.Roster.ToPayload())).Equals(row.GetProperty("roster_sha256").GetString(),StringComparison.OrdinalIgnoreCase),"complete applied native table hash");
            Check(stage.Rows.Select(value=>Convert.ToHexString(Encoding.Unicode.GetBytes(value.Name)).ToLowerInvariant()).SequenceEqual(row.GetProperty("names").EnumerateArray().Select(value=>value.GetString()!)),"native per-slot names");
            Check(stage.JoinNotices.SequenceEqual(row.GetProperty("notices").EnumerateArray().Select(value=>value.GetByte())),"byte-six presence, previous presence, local slot and flag28 notices");
            Check(stage.RefreshRequested && row.GetProperty("refresh_count").GetInt32()==1 && stage.RequiresAcceptedContinuation,"refresh boundary and remaining admission");
            var ui = new List<uint[]> { new uint[]{1,1,0},new uint[]{1,1,0} };
            var profiles = new List<WorldRosterAppliedRow>();
            foreach (var item in stage.Rows)
            {
                if (item.Name.Length!=0)
                {
                    var style=item.LowBandwidth?437u:0;
                    ui.Add([2,item.Slot,0]);ui.Add([2,item.Slot,style]);ui.Add([17,item.Slot,style]);ui.Add([17,item.Slot,style]);
                }
                if(item.HasProfile){profiles.Add(item);ui.Add([0,0,0]);}
            }
            ui.Add([16,0,0]);ui.Add([16,0,0]);
            var expected=row.GetProperty("ui").EnumerateArray().ToArray();
            Check(ui.Count==expected.Length,"ordered supplied UI-service count");
            for(var i=0;i<ui.Count;i++)Check(ui[i].SequenceEqual(expected[i].EnumerateArray().Select(value=>value.GetUInt32())),"native UI header/style operands");
            var nativeProfiles=row.GetProperty("profiles").EnumerateArray().ToArray();
            Check(profiles.Count==nativeProfiles.Length,"profile-event constructor count");
            for(var i=0;i<profiles.Count;i++)
            {
                var item=profiles[i];var profile=nativeProfiles[i];
                Check(item.Slot==profile.GetProperty("slot").GetByte() && Convert.ToHexString(SHA256.HashData(stage.Roster.GetEntry(item.Slot).Profile)).Equals(profile.GetProperty("profile_sha256").GetString(),StringComparison.OrdinalIgnoreCase),"native by-value profile bytes and slot");
            }
        }
        return checks;
        void Check(bool pass,string why){if(!pass)throw new Exception("Roster consumer: "+why);checks++;}
    }
    public static byte[] WireExample(string root)
    {
        using var native=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-roster-consumer.json")));
        return Convert.FromHexString(native.RootElement.GetProperty("tables").GetProperty("presence-only").GetString()!);
    }
}
