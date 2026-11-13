using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class StartupEffectWorldChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-startup-effect-factory.json")));
        var source = fixture.RootElement;
        var definitions = StartupEffectFactoryChecks.Definitions(source);
        var command = new SacredScriptCommand(0xA27C, Convert.FromHexString(source.GetProperty("command_hex").GetString()!));
        var creation = SacredScriptObjectCreationReader.Read(command, _ => throw new Exception("Unexpected symbolic argument."));
        var descriptor = new ItemsPakEntry(new((ushort)creation.TypeId,0,128,0),
            MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(Convert.FromHexString(source.GetProperty("descriptor_hex").GetString()!)), "native");
        var keyx = new byte[256+768];
        BinaryPrimitives.WriteUInt32LittleEndian(keyx.AsSpan(4),1);
        BinaryPrimitives.WriteUInt32LittleEndian(keyx.AsSpan(256+0x24),1);
        var grid = SacredSectorIndex.FromPayload(keyx).CreateGrid();
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        foreach (var row in source.GetProperty("cases").EnumerateArray().Where(value => value.GetProperty("mode").GetString() == "creation"))
        {
            var references = new GoldObjectReferencePool();
            var grant = new byte[8]; BinaryPrimitives.WriteUInt32LittleEndian(grant,44); BinaryPrimitives.WriteUInt32LittleEndian(grant.AsSpan(4),45);
            references.Apply(GoldReferenceBatch.FromPayload(grant));
            var counter = new WorldObjectAllocationCounter(27);
            var activation = new WorldObjectActivationQueue();
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var world = new WorldScriptEffectWorld(definitions,WorldStartupEffectProjection.ComparedGold,references,counter,
                activation,random,new(1,1,false,0,()=>1000),grid,_=>{});
            world.Create(command,creation,descriptor);
            var effect = world.Created.Single();
            var expected = Convert.FromHexString(row.GetProperty("publications")[0].GetProperty("payload_hex").GetString()!);
            foreach (var (offset,length) in new[] { (71,12),(115,24),(268,3),(311,72),(417,12),(437,8) }) expected.AsSpan(offset,length).Clear();
            Check(effect.State.ToPayload().AsSpan().SequenceEqual(expected), "Shared command produces the complete native fixed Effect stream.");
            Check(effect.Publication.Payload.AsSpan(24).SequenceEqual(expected) && effect.PacketReady,
                "Publication retains the complete independently compared state privately.");
            Check(counter.Value == 28 && references.Available == 1 && references.TryAllocate(out var next) && next == 45,
                "Particle system consumes no second reference or shared object allocation.");
            Check(effect.RandomStateAfter == row.GetProperty("rng_after").GetUInt32(), "Shared command retains original constructor/Start RNG ordering.");
            var record = Convert.FromHexString(row.GetProperty("record_hex").GetString()!);
            var started = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(record.AsSpan(8)));
            var due = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(record.AsSpan(16)));
            Check(!effect.Node.Active && effect.Node.PeriodMilliseconds == 25 && effect.Node.StartedAt == 1 &&
                activation.ActivationCount == 1 && activation.Nodes.Count == 0 && activation.Records.Single().Flags == record[4] &&
                activation.Records.Single().StartedAt == started && activation.Records.Single().NextDue == due && effect.Node.NextDue == due,
                "Initial activation and direct placement leave a dormant FX node and original scheduler tombstone.");
            Check(effect.State.IsSystemActive, "Independent particle system stays active after object-node deactivation.");
        }
        return checks;
    }
}
