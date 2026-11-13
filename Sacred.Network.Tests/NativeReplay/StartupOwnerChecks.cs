using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin.Balance;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Scripting;
using Sacred.GameplayHost.Equipment;
using Sacred.Network.Gameplay;

internal static class StartupOwnerChecks
{
    public static int Run(string root)
    {
        using var fixture = Read(root);
        var data = EquipmentGenerationFixture.ReadStartup(root);
        var image = new byte[0x630000]; Bytes(fixture.RootElement,"owner_code_hex").CopyTo(image,0x62624);
        var definitions = SacredHorseOwnerDefinitions.Read(image);
        var balance = HorseValueBalanceBinReader.Read(Bytes(fixture.RootElement,"balance_hex"));
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray().Where(value=>value.GetProperty("requested").GetInt32()==0 || value.GetProperty("requested").GetInt32()==value.GetProperty("level").GetInt32()))
        {
            var horse = Actor(Bytes(row.GetProperty("before"),"horse")); var owner = Actor(Bytes(row.GetProperty("before"),"merchant"));
            var (next,nextOwner) = WorldHorseOwnerAssignment.Apply(horse,owner,balance,definitions,SacredX87Precision.Single);
            var expectedHorse = Bytes(row.GetProperty("after"),"horse"); var expectedOwner = Bytes(row.GetProperty("after"),"merchant");
            Block(next.Component.Values,expectedHorse.AsSpan(0x3b0,238),"complete Horse values");
            Block(next.Component.Health,expectedHorse.AsSpan(0x4d0,24),"complete Horse health");
            Block(nextOwner.Component.Values,expectedOwner.AsSpan(0x3b0,238),"complete owner values");
            Check(next.Component.ResultValid == expectedHorse[0x4cf] && nextOwner.Component.ResultValid == expectedOwner[0x4cf],"result validity");
            Check(next.Base.Flags == Word(expectedHorse,20) && next.DialogIndex == Word(expectedHorse,0x245),"Horse flag/dialog");
            Check(next.PartnerReference == Word(expectedHorse,0x570) && nextOwner.PartnerReference == Word(expectedOwner,0x570),"reciprocal partners");
            Check(row.GetProperty("rng_before").GetUInt32()==row.GetProperty("rng_after").GetUInt32(),"native consumes no RNG");
            var descriptor = data.Templates.GetItem(next.Base.ObjectType);
            var state = WorldCreatureInitialSerialization.Write(next,descriptor,new Dictionary<uint,WorldEquipmentInitialItem>(),data.Templates.GetItem);
            var expectedFull = Bytes(row.GetProperty("final_publication"),"payload_hex"); var body = state.ToPayload();
            Block(body.AsSpan(0,109),expectedFull.AsSpan(0,109),"complete updated Horse stream prefix");
            Block(body.AsSpan(121),expectedFull.AsSpan(121),"complete updated Horse stream including partner");
            var publication = GoldCreatureUpdateCodec.CreateState(next.Base.Owner,next.Base.ObjectReference,next.Base.ObjectType,GoldCreatureUpdateState.FromCreature(state));
            var expectedPublication = row.GetProperty("publications").EnumerateArray().Single();
            Check(publication.Opcode == expectedPublication.GetProperty("opcode").GetUInt16(),"actual unsuppressed state opcode");
            Block(publication.Payload,Bytes(expectedPublication,"prefix_hex").Concat(Bytes(expectedPublication,"payload_hex")).ToArray(),"complete actual startup state publication");
        }
        using var replacements = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-startup-owner-replacement.json")));
        foreach (var row in replacements.RootElement.GetProperty("cases").EnumerateArray())
        {
            var (horse,owner,previous) = WorldHorseOwnerAssignment.ApplyReplacing(Actor(Bytes(row.GetProperty("before"),"horse")),
                Actor(Bytes(row.GetProperty("before"),"merchant")),Actor(Bytes(row.GetProperty("before"),"old_horse")),balance,definitions,SacredX87Precision.Single);
            var expected = row.GetProperty("after");
            Check(horse.PartnerReference==Word(Bytes(expected,"horse"),0x570) && owner.PartnerReference==Word(Bytes(expected,"merchant"),0x570),"replacement reciprocal links");
            var old=Bytes(expected,"old_horse");
            Check(previous.PartnerReference==Word(old,0x570) && previous.Base.Flags==Word(old,20),"previous Horse detach flag/link");
            Block(horse.Component.Values,Bytes(expected,"horse").AsSpan(0x3b0,238),"replacement final values");
            var messages=row.GetProperty("publications");
            Block(GoldCreatureMountCodec.Create(horse.Base.Owner,0,previous.Base.ObjectReference).Payload,Bytes(messages[0],"payload_hex"),"actual native detach argument order");
            var state=WorldCreatureInitialSerialization.Write(horse,data.Templates.GetItem(horse.Base.ObjectType),new Dictionary<uint,WorldEquipmentInitialItem>(),data.Templates.GetItem);
            var update=GoldCreatureUpdateCodec.CreateState(horse.Base.Owner,horse.Base.ObjectReference,horse.Base.ObjectType,GoldCreatureUpdateState.FromCreature(state));
            Block(update.Payload,Bytes(messages[1],"prefix_hex").Concat(Bytes(messages[1],"payload_hex")).ToArray(),"replacement dialog publication");
        }
        return checks;
        void Check(bool pass,string field) { if (!pass) throw new Exception("Startup owner: "+field);checks++; }
        void Block(ReadOnlySpan<byte> actual,ReadOnlySpan<byte> expected,string field)
        {
            if (!actual.SequenceEqual(expected)) throw new Exception("Startup owner: "+field+": "+Convert.ToHexString(actual)+" != "+Convert.ToHexString(expected));
            checks += actual.Length;
        }
    }
    public static byte[] WireExample(string root)
    {
        using var fixture = Read(root); var row=fixture.RootElement.GetProperty("cases")[0].GetProperty("publications")[0];
        return Bytes(row,"prefix_hex").Concat(Bytes(row,"payload_hex")).ToArray();
    }
    public static byte[] ReplacementWireExample(string root)
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-startup-owner-replacement.json")));
        return Bytes(fixture.RootElement.GetProperty("cases")[0].GetProperty("publications")[0],"payload_hex");
    }
    private static WorldCreatureInitialActor Actor(byte[] b) => new(new(Word(b,12),Word(b,16),Word(b,20),Short(b,24),Int(b,28),Int(b,32),b[36],Word(b,40),Word(b,52),b[56],b[57],Word(b,60)),
        new(b.AsSpan(0x3b0,238).ToArray(),b.AsSpan(0x4d0,24).ToArray(),b[0x4ce],b[0x4cf]),Word(b,0x1f0),Word(b,0x1f4),Short(b,0x25f),Short(b,0x574),BitConverter.UInt32BitsToSingle(Word(b,0x208)),Word(b,0x56c),[])
    {
        Direction = new(BitConverter.UInt32BitsToSingle(Word(b,0x70)),BitConverter.UInt32BitsToSingle(Word(b,0x74)),BitConverter.UInt32BitsToSingle(Word(b,0x78))),
        ScriptInitialPosition = new(Int(b,0x2bd),Int(b,0x2c1),b[0x2c5],Short(b,0x2b9)),
        ScriptGroup=Word(b,0x249),RuntimeFlags=Word(b,0x200),Flags2B7=Short(b,0x2b7),DialogIndex=Word(b,0x245),PartnerReference=Word(b,0x570)
    };
    private static JsonDocument Read(string root) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-startup-owner.json")));
    private static byte[] Bytes(JsonElement row,string key)=>Convert.FromHexString(row.GetProperty(key).GetString()!);
    private static uint Word(byte[] b,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
    private static ushort Short(byte[] b,int at)=>BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at));
    private static int Int(byte[] b,int at)=>BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at));
}
