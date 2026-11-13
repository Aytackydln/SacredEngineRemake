using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin;
using Sacred.Core.GameRes;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;
using Sacred.Core.World;
using Sacred.GameplayHost;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class ElevatedBubblePrisonChecks
{
    public static int Run(string root)
    {
        using var source = Read(root);
        var image = Image(source.RootElement);
        var definitions = SacredBubblePrisonDefinitions.Read(image);
        using var fixedSource = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-startup-effect-factory.json")));
        var fixedDefinitions = StartupEffectFactoryChecks.Definitions(fixedSource.RootElement);
        var descriptor = new ItemsPakEntry(new((ushort)definitions.Resource, 0, 128, 0),
            MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(Convert.FromHexString(source.RootElement.GetProperty("descriptor_hex").GetString()!)), "original");
        var keyx = new byte[256 + 768]; BinaryPrimitives.WriteUInt32LittleEndian(keyx.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(keyx.AsSpan(256 + 0x24), 1);
        BinaryPrimitives.WriteInt32LittleEndian(keyx.AsSpan(256 + 0x3c), SacredWorldCoordinates.TileCenterToWorld(44 * 64) - 25);
        BinaryPrimitives.WriteInt32LittleEndian(keyx.AsSpan(256 + 0x40), SacredWorldCoordinates.TileCenterToWorld(49 * 64) - 25);
        var grid = SacredSectorIndex.FromPayload(keyx).CreateGrid();
        var checks = 0;
        Check(definitions.Resource == 853 && definitions.Fx.EventCode == 77, "resource/event identity extracted through original code linkage");
        foreach (var row in source.RootElement.GetProperty("cases").EnumerateArray())
        {
            var command = new Sacred.Core.GameBin.Scripts.SacredScriptCommand(0x75c4f, Convert.FromHexString(row.GetProperty("command_hex").GetString()!));
            var positions = new WorldScriptDefinitions(); positions.Import(new[]{WorldScriptDefinition.FromPayload(Convert.FromHexString(row.GetProperty("definition_hex").GetString()!))});
            var creation = SacredScriptObjectCreationReader.Read(command, name => positions.ResolvePosition(name));
            var references = new GoldObjectReferencePool(); references.Apply(GoldReferenceBatch.FromPayload(BitConverter.GetBytes(44u)));
            var counter = new WorldObjectAllocationCounter(27); var activation = new WorldObjectActivationQueue();
            var seed = row.GetProperty("seed").GetUInt32(); var random = new SacredRandomSequence(seed);
            var runtime = new WorldScriptCreatureRuntime(1, 1, false, 0, () => 1000) { EffectDetailLevel = row.GetProperty("detail").GetByte() };
            var world = new WorldScriptEffectWorld(fixedDefinitions, WorldStartupEffectProjection.ComparedGold,
                references, counter, activation, random, runtime, grid, _ => { }) { BubblePrison = definitions };
            var resources = new WorldScriptItemResources(new(), GameResStore.Empty);
            world.Create(command, creation, descriptor, resources, 0, _ => null);
            var actual = world.Created.Single();
            var publication = row.GetProperty("publications")[0];
            var expected = Convert.FromHexString(publication.GetProperty("payload_hex").GetString()!);
            var opaque = GoldObjectFxState.FromPayload(expected); var particle = GoldParticleSnapshot.FromPayload(opaque.ParticleState.Span);
            Check(expected.Length == 523 && !particle.HasDerivedMarker && particle.DerivedState.Length == GoldBubblePrisonParticleLayout.DerivedSize,
                "native complete stream framing");
            Check(particle.ToPayload().AsSpan().SequenceEqual(opaque.ParticleState.Span), "lossless received particle backing");
            foreach (var (offset, length) in new[] { (71,12), (115,24), (236,3), (383,72), (489,12), (509,8) }) expected.AsSpan(offset, length).Clear();
            var generated = actual.State.ToPayload();
            Check(actual.BaseState.Surface==row.GetProperty("surface").GetByte() && GoldParticleSnapshot.FromPayload(actual.State.ParticleState.Span).Position.Z==0,"unloaded surface copy is independent of initial particle height");
            Check(generated.AsSpan().SequenceEqual(expected), "complete stream; differences " + string.Join(",", Enumerable.Range(0, expected.Length).Where(i => generated[i] != expected[i]).Take(8).Select(i => $"{i:X}:{generated[i]:X2}/{expected[i]:X2}")));
            Check(actual.Publication.Payload.AsSpan(0,24).SequenceEqual(Convert.FromHexString(publication.GetProperty("prefix_hex").GetString()!)) &&
                actual.Publication.Payload.AsSpan(24).SequenceEqual(expected), "native publication prefix and generated state");
            Check(row.GetProperty("rng_calls").GetArrayLength() == 32 && random.State == row.GetProperty("rng_after").GetUInt32() && actual.RandomStateAfter == random.State,
                "32 shared RNG draws including overwritten tenth ring");
            Check(references.Available == 0 && counter.Value == 28 && !actual.Node.Active && actual.Node.PeriodMilliseconds == 25,
                "single reservation/counter and dormant scheduler node");
            var names = row.GetProperty("local_names");
            Check(resources.Replica.Names.Count == names.GetArrayLength() && resources.Replica.Names.Select((value, index) =>
                value.Name == names[index].GetProperty("name").GetString() && value.ResourceId == names[index].GetProperty("resource").GetUInt32() && !value.HasCompleteBytes).All(value => value), "native partial local-name rows");
            var resource = row.GetProperty("primary_resource").GetUInt32();
            Check(actual.BaseState.Word3C == resource && resources.Replica.ObjectBindings.Count == (resource == 0 ? 0 : 1) &&
                (resource == 0 || resources.Replica.ObjectBindings[resource] == 44), "primary resource binding");
            var untouched = new GoldObjectReferencePool(); untouched.Apply(GoldReferenceBatch.FromPayload(BitConverter.GetBytes(45u)));
            var guardedRandom = new SacredRandomSequence(seed); var untouchedCounter = new WorldObjectAllocationCounter(27);
            var guarded = new WorldScriptEffectWorld(fixedDefinitions, WorldStartupEffectProjection.ComparedGold,
                untouched, untouchedCounter, new(), guardedRandom, runtime, grid, _ => { }) { BubblePrison = definitions };
            var untouchedNames = new WorldScriptItemResources(new(), GameResStore.Empty);
            try { guarded.Create(command, creation, descriptor, untouchedNames, 1, _ => null); throw new Exception("Deferred caller admitted"); }
            catch (InvalidDataException) { Check(untouched.Available == 1 && untouchedCounter.Value == 27 && untouchedNames.Replica.Names.Count == 0 && guarded.Created.Count == 0 && guardedRandom.State == seed, "deferred guard before state mutation"); }
        }
        var changed = image.ToArray();
        BinaryPrimitives.WriteSingleLittleEndian(changed.AsSpan(0x8909EC - 0x400000), 2);
        var nativeRings = SacredBubblePrisonInitialization.Create(definitions.Parameters, new(2004));
        var changedRings = SacredBubblePrisonInitialization.Create(SacredBubblePrisonDefinitions.Read(changed).Parameters, new(2004));
        Check(!nativeRings.AngularVelocities.SequenceEqual(changedRings.AngularVelocities), "changed executable velocity coefficient changes output");
        return checks;
        void Check(bool pass, string reason) { if (!pass) throw new Exception("Elevated BUBBLEPRISON: " + reason); checks++; }
    }
    public static byte[] WireExample(string root)
    {
        using var source = Read(root); var definitions = SacredBubblePrisonDefinitions.Read(Image(source.RootElement));
        var row = source.RootElement.GetProperty("cases").EnumerateArray().First(value=>value.GetProperty("surface").GetByte()==1 && value.GetProperty("symbolic").GetBoolean() && value.GetProperty("primary_resource").GetUInt32()!=0);
        var command = new Sacred.Core.GameBin.Scripts.SacredScriptCommand(0,Convert.FromHexString(row.GetProperty("command_hex").GetString()!));
        var positions = new WorldScriptDefinitions(); positions.Import(new[]{WorldScriptDefinition.FromPayload(Convert.FromHexString(row.GetProperty("definition_hex").GetString()!))});
        var creation = SacredScriptObjectCreationReader.Read(command,name=>positions.ResolvePosition(name));
        var keyx=new byte[256];var grid=SacredSectorIndex.FromPayload(keyx).CreateGrid();
        var references=new GoldObjectReferencePool();references.Apply(GoldReferenceBatch.FromPayload(BitConverter.GetBytes(44u)));
        using var fixedSource=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-startup-effect-factory.json")));
        var descriptor = new ItemsPakEntry(new((ushort)definitions.Resource,0,128,0),MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(Convert.FromHexString(source.RootElement.GetProperty("descriptor_hex").GetString()!)),"original");
        var world=new WorldScriptEffectWorld(StartupEffectFactoryChecks.Definitions(fixedSource.RootElement),WorldStartupEffectProjection.ComparedGold,references,new(27),new(),new(row.GetProperty("seed").GetUInt32()),new(1,1,false,0,()=>1000),grid,_=>{}){BubblePrison=definitions};
        world.Create(command,creation,descriptor,new WorldScriptItemResources(new(),GameResStore.Empty),0,_=>null);
        var generated=world.Created.Single().Publication.Payload;
        // This minimal wire-example grid uses sector zero; the independent caller has sector one.
        var expected=Convert.FromHexString(row.GetProperty("publications")[0].GetProperty("payload_hex").GetString()!);
        expected.AsSpan(GoldObjectStateLayout.SectorOffset,2).Clear();
        foreach(var (offset,length) in new[]{(71,12),(115,24),(236,3),(383,72),(489,12),(509,8)})expected.AsSpan(offset,length).Clear();
        if(!generated.AsSpan(24).SequenceEqual(expected))throw new Exception("Uncompared elevated BUBBLEPRISON example");
        return generated;
    }
    private static JsonDocument Read(string root) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-elevated-bubble-prison.json")));
    private static byte[] Image(JsonElement source)
    {
        var segments = source.GetProperty("definition_code").EnumerateArray().ToArray();
        var image = new byte[segments.Max(row => row.GetProperty("offset").GetInt32() + row.GetProperty("hex").GetString()!.Length / 2)];
        foreach (var segment in segments) Convert.FromHexString(segment.GetProperty("hex").GetString()!).CopyTo(image, segment.GetProperty("offset").GetInt32());
        return image;
    }
}
