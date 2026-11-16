using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sacred.GameplayHost.Networking;
using Sacred.GameplayHost.World;
using Sacred.Assets.Executable;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

internal static class AdmissionCompleteChecks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-admission-complete.json")));
        var data=native.RootElement;var checks=0;
        using var identifierEvidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-roster-identifier.json")));
        var identifierData = identifierEvidence.RootElement;
        var image = new byte[identifierData.GetProperty("image_size").GetInt32()];
        foreach (var chunk in identifierData.GetProperty("definition_chunks").EnumerateArray())
            Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
        var identifier = SacredRosterIdentifierDefinitions.Read(image);
        foreach(var row in data.GetProperty("cases").EnumerateArray())
        {
            var previous=new byte[GoldPlayerRosterLayout.Size];
            Convert.FromHexString(data.GetProperty("templates").GetProperty(row.GetProperty("profile_present").GetInt32().ToString()).GetString()!).CopyTo(previous,0);
            var incoming=(GoldAdmissionEvent)GoldGameEventDecoder.Decode(new GoldMessage(0x11,0,0,0,Convert.FromHexString(row.GetProperty("wire_hex").GetString()!)));
            var queries=row.GetProperty("queries");
            var owner = new SacredGameplayReplicaSession();
            owner.Admission.ConfigureIdentifier(identifier.Markup);
            owner.Apply(incoming);
            var receipt = owner.Admission.Current!;
            owner.Admission.CaptureController(receipt, new(new(0xa5,row.GetProperty("old_host").GetByte(),31,0x12345678,31),
                GoldPlayerRoster.FromPayload(previous), new(0,row.GetProperty("flag2").GetByte(),
                    (byte)(row.GetProperty("flag9").GetBoolean()?1:0),(byte)(row.GetProperty("flag28").GetBoolean()?1:0)),
                new(0,0,0,(byte)(row.GetProperty("flag381b").GetBoolean()?1:0),0,0),
                (byte)(row.GetProperty("multiplayer").GetBoolean()?1:0)));
            var result=owner.Admission.Prepare(receipt,queries[0].GetBoolean(),queries[1].GetBoolean(),
                new(0x76543210,7,0x4b,row.GetProperty("connection_flags").GetUInt32(),0x22222222,Encoding.Unicode.GetBytes("Hero"),Encoding.Unicode.GetBytes("Saved")));
            var control=Convert.FromHexString(row.GetProperty("controller_prefix").GetString()!);
            Check(result.Prefix.State.HostByte1==control[1] && result.Flag2==control[2] && result.Prefix.State.PendingByte3==control[3],"native controller bytes across complete handler");
            Check(result.Prefix.State.LocalReference==Word(control,0x14) && result.Prefix.State.HostToken==Word(control,0x24) &&
                result.Prefix.State.ConnectionLocalReference==row.GetProperty("connection_local").GetUInt32(),"identity/token preservation including late rejection");
            var roster=result.AppliedRoster?.Roster.ToPayload()??previous;
            Check(Convert.ToHexString(SHA256.HashData(roster)).Equals(row.GetProperty("roster_sha256").GetString(),StringComparison.OrdinalIgnoreCase),"composed complete native roster hash");
            Check((result.StagedRoster?.Flag48??0)==row.GetProperty("flag48").GetByte() && (result.StagedRoster?.Flag3819??0)==row.GetProperty("flag3819").GetByte(),"staged runtime flags");
            var requests=row.GetProperty("requests").EnumerateArray().ToArray();
            Check(result.Requests.Ui.Count==requests.Length,"ordered UI constructor request count");
            for(var i=0;i<requests.Length;i++)
            {
                var actual=result.Requests.Ui[i];var expected=requests[i];
                Check(actual.EventId==expected.GetProperty("event").GetUInt32() && actual.Target==expected.GetProperty("target").GetString() &&
                    actual.Mode==expected.GetProperty("mode").GetUInt32() && actual.Action==expected.GetProperty("action").GetUInt32() &&
                    actual.ResourceKey==expected.GetProperty("resource").GetString() && actual.Parameter==expected.GetProperty("parameter").GetUInt32(),"native UI operands/resource-key boundary");
            }
            Check(result.Requests.ChatResourceKeys.SequenceEqual(row.GetProperty("notices").EnumerateArray().Select(value=>value.GetString()!)),"native ordered chat-resource requests and flag precedence");
            Check(result.Requests.Native117Requests==row.GetProperty("sent").GetArrayLength() && (result.Requests.FinalizerRequested?1:0)==row.GetProperty("finalizers").GetInt32(),"117/finalizer service branches");
            Check((result.AppliedRoster is null?0:1)==row.GetProperty("refreshes").GetInt32(),"prefix rejection versus applied-before-late-rejection");
            Check(result.RequiresServiceDelivery && result.RequiresActorBinding==(!result.Prefix.Rejected&&!result.LateRejected),"no session/actor readiness inferred from a computed plan");
            var pendingEffects = new List<WorldAdmissionEffect>();
            var resetService = new WorldAdmissionResetService(() => new(1), _ => { });
            var deliveredUi = new List<GoldLobbyUiRequest>();
            var uiDispatcher = new WorldAdmissionUiDispatcher(() => 1, _ => deliveredUi.Add);
            while (owner.Admission.TryPeek(receipt, out var work))
            {
                pendingEffects.Add(work.Effect);
                switch (work.Effect)
                {
                    case WorldAdmissionEffect.ResetService: owner.Admission.DeliverResetService(work, resetService); break;
                    case WorldAdmissionEffect.Ui: owner.Admission.DeliverUi(work, uiDispatcher); break;
                    default: owner.Admission.Report(work, GoldConsumerOutcome.Consumed, "supplied native-fixture service"); break;
                }
            }
            Check(resetService.Current?.EntryFlag0 == 0 && deliveredUi.SequenceEqual(result.Requests.Ui) && uiDispatcher.DirectCalls == result.Requests.Ui.Count,
                "native reset and immediate UI service delivery complete before their receipts advance");
            Check(receipt.DeliveryComplete && owner.Replicas.Objects.Count == 0,
                "session owner tracks complete supplied delivery without fabricating an actor");
            var uiIndex = 0;
            var chatIndex = 0;
            var effects = pendingEffects.Select(effect => effect switch
            {
                WorldAdmissionEffect.ResetService => "controller-reset",
                WorldAdmissionEffect.Roster => "roster",
                WorldAdmissionEffect.Ui => "ui:" + uiIndex++,
                WorldAdmissionEffect.DispatchController117 => "send:117",
                WorldAdmissionEffect.FinalizeConnection => "finalizer",
                WorldAdmissionEffect.Chat => "chat:" + chatIndex++,
                _ => throw new Exception("Unknown admission effect")
            });
            Check(effects.SequenceEqual(row.GetProperty("effects").EnumerateArray().Select(value => value.GetString()!)),
                "native outer service order, including reset, late rejection, send, finalizer and notices");
        }
        return checks;
        void Check(bool pass,string why){if(!pass)throw new Exception("Complete admission: "+why);checks++;}
    }
    private static uint Word(byte[] bytes,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
}
