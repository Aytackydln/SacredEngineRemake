using System.Text.Json;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;

internal static class SessionGameFlagsChecks
{
    public static int Run(string root)
    {
        var checks=0;
        void Check(bool value,string why){if(!value)throw new Exception("Session game flags: "+why);checks++;}
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-session-game-flags.json")));
        foreach(var row in fixture.RootElement.GetProperty("client_cases").EnumerateArray())
        {
            var requested=row.GetProperty("requested").GetUInt32();var enabled=row.GetProperty("enabled").GetInt32()!=0;
            Check(row.GetProperty("after").GetUInt32()==(enabled?requested:row.GetProperty("before").GetUInt32()),"native active flag replacement/inactive preservation");
            var wire=Convert.FromHexString(row.GetProperty("payload_hex").GetString()!);var expected=wire.ToArray();
            var entry=(GoldSessionGameFlagsEvent)GoldGameEventDecoder.Decode(new(0x1e,0,0,2,wire));Array.Fill(wire,(byte)0xcc);
            var world=new GoldWorldReplica();world.Apply(entry);var state=world.Session;
            Check(state.GameFlags==requested&&state.IsLocked==((requested&4)!=0),"complete observed flags and consistent lock state");
            if(enabled)Check(state.NativeControlWord==row.GetProperty("control_word").GetUInt16(),"native active-client control word");
            else Check(row.GetProperty("control_word").GetUInt16()==1,"native inactive query result");
            Check(entry.Message.Payload.SequenceEqual(expected),"owned first word and ignored suffix");
            Check(world.Revision==1&&state.GameFlagsNotificationCount==1&&state.LockNotificationCount==0,"independent notification counters");
            var tracker=new GoldAdmissionTracker();tracker.ProcessBatch([entry.Message]);tracker.Begin();tracker.ProcessBatch([new(0x11,0,0,0,[4,3,0,0,0,0])]);
            Check(tracker.Status.SessionGameFlags==requested&&tracker.Status.SessionLocked==state.IsLocked,"early flags survive admission");
            Check(tracker.Status.Flags==4&&tracker.Status.WorldStartup.HostWorldAvailable&&!tracker.Status.WorldStartup.ParticipantStartApproved,"role/startup flags remain distinct");
            world.Clear();Check(state.GameFlags is null&&state.NativeControlWord is null&&state.GameFlagsNotificationCount==0,"disconnect clears observations");
        }
        foreach(var row in fixture.RootElement.GetProperty("server_cases").EnumerateArray())
        {
            var before=row.GetProperty("before").GetUInt32();var requested=row.GetProperty("requested").GetUInt32();
            var accepted=row.GetProperty("accepted").GetBoolean();var after=row.GetProperty("after").GetUInt32();
            Check(accepted==(row.GetProperty("slot").GetUInt32()==3&&row.GetProperty("client_gate").GetInt32()==0&&(before&8)==0),"native server authorization gates");
            Check(after==(accepted?(requested&0xfffe8f72)|(before&0x1708d):before),"native protected-bit merge or rejected preservation");
            var relays=row.GetProperty("relayed_flags");Check(relays.GetArrayLength()==(accepted?1:0),"native authorized relay only");
            if(accepted)Check(relays[0].GetUInt32()==after,"server broadcasts merged flags, not requested word");
        }
        return checks;
    }
}
