using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin.Balance;
using Sacred.GameplayHost.Equipment;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class ScriptArmorCommandChecks
{
    public static int Run(string root) => Run(root,null);
    public static int RunChain(string root) => Run(root,null,"native-script-armor-chain");
    public static int RunShoulder(string root) => Run(root,null,"native-script-shoulder-command");
    public static byte[] ShoulderWireExample(string root)
    {
        byte[]? payload = null; Run(root,message=>payload ??= message.Payload,"native-script-shoulder-command"); return payload!;
    }
    public static byte[] ChainWireExample(string root,uint type)
    {
        byte[]? payload = null; Run(root,message=> { if (GoldItemState.FromPayload(message.Payload.AsSpan(8)).PhysicalState.BaseState.ObjectType==type) payload ??= message.Payload; },"native-script-armor-chain"); return payload!;
    }
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,Lazy<byte[]>> Wire = new();
    public static byte[] WireExample(string root) => Wire.GetOrAdd(root,path=>new(()=>BuildWireExample(path))).Value.ToArray();
    private static byte[] BuildWireExample(string root)
    {
        byte[]? payload = null;
        Run(root,message=>payload ??= message.Payload);
        return payload!;
    }
    private static int Run(string root,Action<Sacred.Network.Protocol.GoldMessage>? collect,string fixtureName="native-script-armor-command")
    {
        using var fixture = Read(fixtureName);
        using var selection = Read("native-equipment-slot-selection");
        using var generation = Read("native-script-equipment-command-generation");
        var data = EquipmentGenerationFixture.ReadStartup(root);
        var slots = SacredEquipmentSlotDefinitions.Read(Bytes(selection.RootElement,"override_code_hex"));
        var progression = CreatureBalanceBinReader.Read(Bytes(generation.RootElement,"balance_prefix_hex"));
        var checks = 0;
        WorldEquipmentInitialFactory? retainedFactory = null;
        WorldCreatureInitialActor? retainedActor = null;
        GoldObjectReferencePool? retainedReferences = null;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var before = Bytes(row,"actor_before_hex");
            var actor = new WorldCreatureInitialActor(Base(before),new(before.AsSpan(0x3b0,238).ToArray(),before.AsSpan(0x4d0,24).ToArray(),before[0x4ce],before[0x4cf]),
                Word(before,0x1f0),Word(before,0x1f4),0,0,0,0,[])
            {
                Direction = Vector(before,0x70), RuntimeFlags = Word(before,0x200),
                EquipmentReferences = Enumerable.Range(0,19).Select(index=>Word(before,0x1a4+index*4)).ToArray()
            };
            var reference = row.GetProperty("reference").GetUInt32();
            var consecutive = row.GetProperty("installed").GetBoolean() && retainedFactory is not null;
            var references = consecutive ? retainedReferences! : new GoldObjectReferencePool();
            var hands = row.GetProperty("hands").EnumerateArray().OrderBy(hand=>hand.GetProperty("reference").GetUInt32()).ToArray();
            if (!consecutive)
                references.Apply(GoldReferenceBatch.FromPayload(hands.SelectMany(hand=>BitConverter.GetBytes(hand.GetProperty("reference").GetUInt32())).ToArray()));
            references.Apply(GoldReferenceBatch.FromPayload(BitConverter.GetBytes(reference)));
            var factory = consecutive ? retainedFactory! : new WorldEquipmentInitialFactory(references,new((byte)(row.GetProperty("counter_before").GetByte()-hands.Length)),new(),1);
            if (consecutive) actor = retainedActor!;
            else foreach (var hand in hands)
            {
                var bytes = Bytes(hand,"item_hex");var type = Word(bytes,16);
                var held = factory.Create(data.Templates.GetItem(type),data.Templates.GetInstance(type),1,true);
                held.Physical.Base = Base(bytes);bytes.AsSpan(0x118,130).CopyTo(held.Values);
            }
            var random = new Sacred.Core.Runtime.SacredRandomSequence(row.GetProperty("generation").GetProperty("seed").GetUInt32());
            var actual = WorldScriptArmorEquipmentCommand.Apply(actor,data.Templates.GetItem(row.GetProperty("type").GetUInt32()),factory,data,slots,progression,random,1,1,1);
            collect?.Invoke(actual.Publication);
            Check(row.GetProperty("order").EnumerateArray().Select(value=>value.GetString()).SequenceEqual(new[]{"publish","install"}) && row.GetProperty("installation_sends")[0].GetByte()==0,"actual native publication order and startup send suppression");
            var expected = Bytes(row.GetProperty("publications")[0],"payload_hex");
            // Gold's flag-2 unplaced physical cache is discarded by its receiver.
            expected.AsSpan(GoldPhysicalStateLayout.SelectedVectorOffset,12).Clear();
            Check(actual.Item.SerializedState!.ToPayload().AsSpan().SequenceEqual(expected),"entire native pre-install Armor stream");
            Check(actual.Publication.Payload.AsSpan(0,8).SequenceEqual(Bytes(row.GetProperty("publications")[0],"prefix_hex")) && actual.Publication.Payload.AsSpan(8).SequenceEqual(expected),"private publication prefix and body");
            var item = Bytes(row,"item_after_hex");var after = Bytes(row,"actor_after_hex");
            Check(actual.Item.Physical.Base == Base(item),"post-install item identity and flags");
            Check(actual.Item.Values.AsSpan().SequenceEqual(item.AsSpan(0x118,130)),"all installed values after normalization");
            Check(actual.Actor.Base == Base(after) && actual.Actor.EquipmentReferences.SequenceEqual(Enumerable.Range(0,19).Select(index=>Word(after,0x1a4+index*4))),"native actor identity and all nineteen slots");
            Check(actual.Actor.Component.Values.AsSpan().SequenceEqual(after.AsSpan(0x3b0,238)) && ReferenceEquals(actual.Actor.Component,actor.Component),"no invented actor recalculation");
            Check(actual.Item.Physical.AttachmentParent == actor.Base.ObjectReference && actual.Item.Physical.Link == Word(item,0x2c),"attachment identity");
            Check(actual.Item.Physical.Direction == Vector(item,0x70) && actual.Item.Physical.Vector7C == Vector(item,0x7c) && actual.Item.Physical.Vector88 == Vector(item,0x88) && actual.Item.Physical.PhysicalFlags == Word(item,0xb0),"unloaded attachment fields");
            Check(random.State == row.GetProperty("generation").GetProperty("rng_after").GetUInt32() && references.Available==0,"generation RNG and one command allocation");
            if (row.GetProperty("installed").GetBoolean()) (retainedFactory,retainedActor,retainedReferences)=(factory,actual.Actor,references);
            Check(row.GetProperty("calls").EnumerateArray().All(value=>value.GetString()!="0x556f10"),"actual native hands do not detach");
        }
        return checks;
        JsonDocument Read(string name)=>JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/"+name+".json")));
        void Check(bool pass,string why) { if (!pass) throw new Exception("Native Armor command: "+why);checks++; }
    }
    private static byte[] Bytes(JsonElement row,string key)=>Convert.FromHexString(row.GetProperty(key).GetString()!);
    private static uint Word(byte[] bytes,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
    private static GoldObjectBaseState Base(byte[] b)=>new(Word(b,12),Word(b,16),Word(b,20),BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(24)),(int)Word(b,28),(int)Word(b,32),b[36],Word(b,40),Word(b,52),b[56],b[57],Word(b,60));
    private static GoldPhysicalVector Vector(byte[] b,int at)=>new(BitConverter.UInt32BitsToSingle(Word(b,at)),BitConverter.UInt32BitsToSingle(Word(b,at+4)),BitConverter.UInt32BitsToSingle(Word(b,at+8)));
}
