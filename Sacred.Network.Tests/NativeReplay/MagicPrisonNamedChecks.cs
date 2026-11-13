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

internal static class MagicPrisonNamedChecks
{
    public static int Run(string root)
    {
        using var definitionsSource = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-magic-prison-snapshots.json")));
        var segments = definitionsSource.RootElement.GetProperty("definition_code").EnumerateArray().ToArray();
        var image = new byte[segments.Max(row => row.GetProperty("offset").GetInt32() + row.GetProperty("hex").GetString()!.Length / 2)];
        foreach (var segment in segments) Convert.FromHexString(segment.GetProperty("hex").GetString()!).CopyTo(image, segment.GetProperty("offset").GetInt32());
        var definitions = SacredMagicPrisonDefinitions.Read(image);
        using var fixedSource = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-startup-effect-factory.json")));
        var fixedDefinitions = StartupEffectFactoryChecks.Definitions(fixedSource.RootElement);
        var descriptor = new ItemsPakEntry(new((ushort)definitions.Resource, 0, 128, 0),
            MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(Convert.FromHexString(definitionsSource.RootElement.GetProperty("descriptor_hex").GetString()!)), "original");
        var keyx = new byte[256 + 768]; BinaryPrimitives.WriteUInt32LittleEndian(keyx.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(keyx.AsSpan(256 + 0x24), 1);
        BinaryPrimitives.WriteInt32LittleEndian(keyx.AsSpan(256 + 0x3c), SacredWorldCoordinates.TileCenterToWorld(98 * 64) - 25);
        BinaryPrimitives.WriteInt32LittleEndian(keyx.AsSpan(256 + 0x40), SacredWorldCoordinates.TileCenterToWorld(80 * 64) - 25);
        var grid = SacredSectorIndex.FromPayload(keyx).CreateGrid();
        using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-magic-prison-named.json")));
        var checks = 0;
        foreach (var row in source.RootElement.GetProperty("cases").EnumerateArray())
        {
            var command = new Sacred.Core.GameBin.Scripts.SacredScriptCommand(0x2cdd1, Convert.FromHexString(row.GetProperty("command_hex").GetString()!));
            var creation = SacredScriptObjectCreationReader.Read(command, _ => throw new Exception("Unexpected symbolic position"));
            var references = new GoldObjectReferencePool(); references.Apply(GoldReferenceBatch.FromPayload(BitConverter.GetBytes(44u)));
            var counter = new WorldObjectAllocationCounter(27); var activation = new WorldObjectActivationQueue();
            var random = new SacredRandomSequence(2004);
            var detail = row.GetProperty("detail").GetByte();
            var runtime = new WorldScriptCreatureRuntime(1, 1, false, 0, () => 1000) { EffectDetailLevel = detail == 2 ? null : detail };
            var world = new WorldScriptEffectWorld(fixedDefinitions, WorldStartupEffectProjection.ComparedGold,
                references, counter, activation, random, runtime, grid, _ => { }) { MagicPrison = definitions };
            var resources = new WorldScriptItemResources(new(), GameResStore.Empty);
            world.Create(command, creation, descriptor, resources, 0, _ => null);
            var actual = world.Created.Single();
            var expected = Convert.FromHexString(row.GetProperty("publications")[0].GetProperty("payload_hex").GetString()!);
            foreach (var (offset, length) in new[] { (71,12), (115,24), (236,3), (1419,72), (1525,12), (1545,8) }) expected.AsSpan(offset, length).Clear();
            var generated = actual.State.ToPayload();
            Check(generated.AsSpan().SequenceEqual(expected), "complete named stream, differences: " + string.Join(",", Enumerable.Range(0, expected.Length).Where(i => generated[i] != expected[i]).Take(8).Select(i => $"{i:X}:{generated[i]:X2}/{expected[i]:X2}")));
            Check(actual.Publication.Payload.AsSpan(0,24).SequenceEqual(Convert.FromHexString(row.GetProperty("publications")[0].GetProperty("prefix_hex").GetString()!)) &&
                actual.Publication.Payload.AsSpan(24).SequenceEqual(expected), "complete native publication prefix and privately retained state");
            Check(random.State == 2004 && references.Available == 0 && counter.Value == 28 && !actual.Node.Active && actual.Node.PeriodMilliseconds == 25, "shared reservation/counter, dormant node and no RNG draws");
            var names = row.GetProperty("local_names");
            Check(resources.Replica.Names.Count == names.GetArrayLength() && resources.Replica.Names.Select((value, index) =>
                value.Name == names[index].GetProperty("name").GetString() && value.ResourceId == names[index].GetProperty("resource").GetUInt32() && !value.HasCompleteBytes).All(value => value), "native local-name reservation with unresolved scratch provenance retained");
            var resource = row.GetProperty("primary_resource").GetUInt32();
            Check(actual.BaseState.Word3C == resource && resources.Replica.ObjectBindings.Count == (resource == 0 ? 0 : 1) &&
                (resource == 0 || resources.Replica.ObjectBindings[resource] == 44), "native primary resource and map binding; secondary is not a local reservation");
            foreach (var (flags, overrideDetail) in new (uint, byte?)[] { (1, null), (0, 3) })
            {
                var untouched = new GoldObjectReferencePool(); untouched.Apply(GoldReferenceBatch.FromPayload(BitConverter.GetBytes(45u)));
                var untouchedCounter = new WorldObjectAllocationCounter(27);
                var guarded = new WorldScriptEffectWorld(fixedDefinitions, WorldStartupEffectProjection.ComparedGold,
                    untouched, untouchedCounter, new(), random, runtime with { EffectDetailLevel = overrideDetail }, grid, _ => { }) { MagicPrison = definitions };
                var untouchedNames = new WorldScriptItemResources(new(), GameResStore.Empty);
                try { guarded.Create(command, creation, descriptor, untouchedNames, flags, _ => null); throw new Exception("Unsupported caller admitted"); }
                catch (Exception error) when (error is InvalidDataException or NotSupportedException)
                { Check(untouched.Available == 1 && untouchedCounter.Value == 27 && untouchedNames.Replica.Names.Count == 0 && guarded.Created.Count == 0 && random.State == 2004, "deferred/unsupported-detail rejection before names, reservation, counter or RNG"); }
            }
        }
        return checks;
        void Check(bool pass, string reason) { if (!pass) throw new Exception("Named MAGICPRISON: " + reason); checks++; }
    }

    /// <summary>Production serializer output with independently compared native
    /// identity/projection inputs, for shared envelope/dispatch/TCP contracts.</summary>
    public static byte[] WireExample(string root)
    {
        using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-magic-prison-snapshots.json")));
        var segments = source.RootElement.GetProperty("definition_code").EnumerateArray().ToArray();
        var image = new byte[segments.Max(row => row.GetProperty("offset").GetInt32() + row.GetProperty("hex").GetString()!.Length / 2)];
        foreach (var segment in segments) Convert.FromHexString(segment.GetProperty("hex").GetString()!).CopyTo(image, segment.GetProperty("offset").GetInt32());
        var definitions = SacredMagicPrisonDefinitions.Read(image);
        using var named = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-magic-prison-named.json")));
        var row = named.RootElement.GetProperty("cases").EnumerateArray().First(value => value.GetProperty("primary").GetString() == "obj_UW12_Door" &&
            value.GetProperty("secondary").GetString() == "res:19582" && value.GetProperty("detail").GetByte() == 2);
        var native = GoldObjectFxState.FromPayload(Convert.FromHexString(row.GetProperty("publications")[0].GetProperty("payload_hex").GetString()!));
        var projected = GoldParticleSnapshot.FromPayload(native.ParticleState.Span).Position;
        var generated = WorldMagicPrisonEffectSerialization.Create(definitions, native.BaseState, projected, 2);
        return GoldObjectStateCodec.Create(0x16f, 1, generated.ToPayload()).Payload;
    }
}
