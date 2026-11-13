using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin.Balance;
using Sacred.Assets.Paks.Creature;
using Sacred.GameplayHost.Equipment;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class ScriptArmorRefreshChecks
{
    public static int Run(string root) => Run(root, null);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,Lazy<byte[]>> Wire = new();
    public static byte[] RemovalWireExample(string root) => Wire.GetOrAdd(root,path=>new(()=>BuildWire(path))).Value.ToArray();
    private static byte[] BuildWire(string root)
    {
        byte[]? result = null;
        Run(root, bytes => result ??= bytes);
        return result!;
    }
    private static int Run(string root, Action<byte[]>? collect)
    {
        JsonDocument Read(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/" + name + ".json")));
        using var fixture = Read("native-script-armor-refresh-command");
        using var basis = Read("native-creature-base-results");
        using var initialization = Read("native-creature-result-initialization");
        using var finalization = Read("native-creature-result-finalization");
        using var precision = Read("native-startup-floating-point");
        using var factors = Read("native-special-health-factor");
        using var selection = Read("native-equipment-slot-selection");
        var image = new byte[basis.RootElement.GetProperty("image_size").GetInt32()];
        foreach (var source in new[] { basis, initialization, finalization, precision, factors })
            foreach (var chunk in source.RootElement.GetProperty("definition_chunks").EnumerateArray())
                Bytes(chunk, "hex").CopyTo(image, chunk.GetProperty("offset").GetInt32());
        var balance = Bytes(factors.RootElement, "balance_hex");
        var results = WorldCreatureScalarResultAssets.Read(image, balance);
        var progression = CreatureBalanceBinReader.Read(balance);
        var slots = SacredEquipmentSlotDefinitions.Read(Bytes(selection.RootElement, "override_code_hex"));
        var creatures = CreaturePakArchive.Parse(Bytes(fixture.RootElement, "creatures_hex"));
        var data = EquipmentGenerationFixture.ReadStartup(root);
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var before = Bytes(row, "actor_before_hex");
            var actor = new WorldCreatureInitialActor(Base(before), new(before.AsSpan(0x3b0,238).ToArray(), before.AsSpan(0x4d0,24).ToArray(), before[0x4ce], before[0x4cf]),
                Word(before,0x1f0), Word(before,0x1f4), 0,0,0,0,[])
            {
                Direction = Vector(before,0x70), RuntimeFlags = Word(before,0x200),
                EquipmentReferences = Enumerable.Range(0,19).Select(i=>Word(before,0x1a4+i*4)).ToArray()
            };
            var hands = row.GetProperty("hands").EnumerateArray().ToArray();
            var references = new GoldObjectReferencePool();
            references.Apply(GoldReferenceBatch.FromPayload(hands.SelectMany(h=>BitConverter.GetBytes(h.GetProperty("reference").GetUInt32())).Concat(BitConverter.GetBytes(row.GetProperty("reference").GetUInt32())).ToArray()));
            var factory = new WorldEquipmentInitialFactory(references,new((byte)(row.GetProperty("counter_before").GetByte()-hands.Length)),new(),1);
            foreach (var hand in hands)
            {
                var bytes = Bytes(hand,"item_hex"); var type = Word(bytes,16);
                var held = factory.Create(data.Templates.GetItem(type),data.Templates.GetInstance(type),1,true);
                WorldScriptEquipmentGeneration.Apply(held,1,1,data,new(2004),Sacred.Core.Runtime.SacredX87Precision.Single);
                held.Physical.Base = Base(bytes); held.Physical.SetLink(Word(bytes,0x2c),true);
                held.Physical.Direction = Vector(bytes,0x70);held.Physical.Vector7C = Vector(bytes,0x7c);held.Physical.Vector88 = Vector(bytes,0x88);
                held.Physical.PhysicalFlags = Word(bytes,0xb0); bytes.AsSpan(0x118,130).CopyTo(held.Values);
            }
            var generation = row.GetProperty("generation");
            var random = new Sacred.Core.Runtime.SacredRandomSequence(generation.GetProperty("seed").GetUInt32());
            var actual = WorldScriptArmorEquipmentCommand.Apply(actor,data.Templates.GetItem(row.GetProperty("type").GetUInt32()),factory,data,slots,progression,random,1,1,1,
                new(creatures.ByItemType[actor.Base.ObjectType],results,1,1));
            var after = Bytes(row,"actor_after_hex");
            Check(actual.Actor.Base == Base(after),"actor base");
            Check(actual.Actor.EquipmentReferences.SequenceEqual(Enumerable.Range(0,19).Select(i=>Word(after,0x1a4+i*4))),"nineteen slots and no restoration");
            Check(actual.Actor.Component.Values.AsSpan().SequenceEqual(after.AsSpan(0x3b0,238)),"every result value: " + string.Join(",",Enumerable.Range(0,238).Where(i=>actual.Actor.Component.Values[i]!=after[0x3b0+i]).Select(i=>$"{i:X}:{actual.Actor.Component.Values[i]:X2}/{after[0x3b0+i]:X2}")));
            Check(actual.Actor.Component.Health.AsSpan().SequenceEqual(after.AsSpan(0x4d0,24)),"health");
            Check(actual.Actor.Component.ResultValid==after[0x4cf] && actual.Actor.RuntimeFlags==Word(after,0x200),"result validity and runtime flags");
            var item = Bytes(row,"item_after_hex");
            Check(actual.Item.Physical.Base==Base(item) && actual.Item.Values.AsSpan().SequenceEqual(item.AsSpan(0x118,130)),"installed Armor");
            foreach (var hand in row.GetProperty("hands_after").EnumerateArray())
            {
                var bytes = Bytes(hand,"item_hex");var held = factory.Items[hand.GetProperty("reference").GetUInt32()];
                Check(held.Physical.Base==Base(bytes) && held.Physical.Link==Word(bytes,0x2c),"detached base/link");
                Check(held.Values.AsSpan().SequenceEqual(bytes.AsSpan(0x118,130)),"detached own values");
                Check(held.Physical.Direction==Vector(bytes,0x70) && held.Physical.Vector7C==Vector(bytes,0x7c) && held.Physical.Vector88==Vector(bytes,0x88) && held.Physical.PhysicalFlags==Word(bytes,0xb0),"detached model fields");
            }
            var published = row.GetProperty("publications");
            Check(published.GetArrayLength()==actual.Publications.Count && actual.Publications.Select(m=>m.Opcode).SequenceEqual(new ushort[]{0x16e}.Concat(Enumerable.Repeat((ushort)0x11d,hands.Length))),"publication order");
            var body = Bytes(published[0],"payload_hex");body.AsSpan(GoldPhysicalStateLayout.SelectedVectorOffset,12).Clear();
            Check(actual.Publication.Payload.AsSpan(8).SequenceEqual(body),"incoming full item stream");
            for(var i=1;i<actual.Publications.Count;i++)
            {
                var request = Bytes(published[i],"request_hex");
                Check(actual.Publications[i].Payload.AsSpan().SequenceEqual(request.AsSpan(8,12)),"native removal payload and right-before-left order");
            }
            Check(random.State==generation.GetProperty("rng_after").GetUInt32() && references.Available==0,"generation RNG and allocation");
            collect?.Invoke(actual.Publications[1].Payload);
        }
        return checks;
        void Check(bool pass,string why) { if(!pass) throw new Exception("Native Armor hand refresh: "+why);checks++; }
    }
    private static byte[] Bytes(JsonElement row,string key)=>Convert.FromHexString(row.GetProperty(key).GetString()!);
    private static uint Word(byte[] b,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
    private static GoldObjectBaseState Base(byte[] b)=>new(Word(b,12),Word(b,16),Word(b,20),BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(24)),(int)Word(b,28),(int)Word(b,32),b[36],Word(b,40),Word(b,52),b[56],b[57],Word(b,60));
    private static GoldPhysicalVector Vector(byte[] b,int at)=>new(BitConverter.UInt32BitsToSingle(Word(b,at)),BitConverter.UInt32BitsToSingle(Word(b,at+4)),BitConverter.UInt32BitsToSingle(Word(b,at+8)));
}
