using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.Paks.Creature;
using Sacred.Assets.Paks.Weapon;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class HostAmbientFactoryChecks
{
    public static int Run(string root)
    {
        using var proof = Read("native-host-ambient-factory");
        using var constructor = Read("native-creature-constructor"); using var horse = Read("native-horse-constructor");
        using var basis = Read("native-creature-base-results"); using var initialization = Read("native-creature-result-initialization");
        using var finalization = Read("native-creature-result-finalization"); using var precision = Read("native-startup-floating-point");
        var image = new byte[basis.RootElement.GetProperty("image_size").GetInt32()];
        foreach (var source in new[] { constructor, horse, basis, initialization, finalization, precision })
            foreach (var chunk in source.RootElement.GetProperty("definition_chunks").EnumerateArray())
                Bytes(chunk, "hex").CopyTo(image, chunk.GetProperty("offset").GetInt32());
        var results = WorldCreatureScalarResultAssets.Read(image, Bytes(horse.RootElement, "balance_hex"));
        var definitions = SacredCreatureConstructionDefinitions.Read(image);
        var keyx = KeyxSectorRecord.FromBytes(Bytes(proof.RootElement, "keyx_hex"));
        using var compressed = new MemoryStream(Bytes(proof.RootElement, "compressed_hex"));
        using var inflate = new ZLibStream(compressed, CompressionMode.Decompress); using var output = new MemoryStream(); inflate.CopyTo(output);
        var terrain = output.ToArray();
        var checks = 0;
        foreach (var row in proof.RootElement.GetProperty("cases").EnumerateArray())
        {
            var archive = new byte[256 + CreatureGoldRecordLayout.Size * 2];
            BinaryPrimitives.WriteUInt32LittleEndian(archive, 0x00464943); BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(4), 2);
            Bytes(row, "template_hex").CopyTo(archive, 256 + CreatureGoldRecordLayout.Size);
            var template = CreaturePakArchive.Parse(archive).Entries[1];
            var descriptor = new ItemsPakEntry(new((ushort)template.ItemType, 0, 128, 0), MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(Bytes(row, "descriptor_hex")), "");
            var references = new GoldObjectReferencePool(); references.Apply(GoldReferenceBatch.FromPayload(BitConverter.GetBytes(86u)));
            var counter = new WorldObjectAllocationCounter(69); var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var factory = new WorldCreatureInitialFactory(results, definitions, references, counter, random, 1, 1);
            var actor = factory.Create(template, descriptor, 1000, 1, true, true);
            var bytes = Bytes(row, "position_hex");
            var position = new GoldObjectPosition(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)), BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8)), bytes[12], BinaryPrimitives.ReadUInt16LittleEndian(bytes));
            var x = SacredWorldCoordinates.WorldToTile(position.X) - SacredSectorCoordinates.ToTileOrigin(keyx.RawX);
            var y = SacredWorldCoordinates.WorldToTile(position.Y) - SacredSectorCoordinates.ToTileOrigin(keyx.RawY);
            var tile = WldxTileRecord.FromBytes(terrain.AsSpan(keyx.TilesRelativeOffset + (y * 64 + x) * 32));
            uint head = 0;
            actor = WorldCreatureAmbientPlacement.Place(actor, new(position, 0, 0, tile, reference => head = reference));
            var state = WorldCreatureInitialSerialization.Write(actor, descriptor, new Dictionary<uint, Sacred.GameplayHost.Equipment.WorldEquipmentInitialItem>(), _ => descriptor);
            actor = actor with { Base = actor.Base with { Flags = actor.Base.Flags & ~0x10000u } }; factory.Update(actor);
            var native = Bytes(row, "actor_hex");
            Check(actor.Component.Values.SequenceEqual(native.AsSpan(0x3b0, 238).ToArray()) && actor.Component.Health.SequenceEqual(native.AsSpan(0x4d0, 24).ToArray()), "all constructor result/health bytes");
            var managedPayload = state.ToPayload(); var nativePayload = Bytes(row.GetProperty("publications")[0], "payload_hex");
            // Existing creature consumer proof establishes that Reset3DPosition
            // discards this failed zero-handle bone cache. No other bytes normalize.
            nativePayload.AsSpan(GoldPhysicalStateLayout.SelectedVectorOffset, 12).Clear();
            Check(managedPayload.SequenceEqual(nativePayload), "entire native 16F body, with only unused bone-cache normalization");
            Check(actor.Base.Flags == BinaryPrimitives.ReadUInt32LittleEndian(native.AsSpan(20)) && actor.Node.Flags == native[8] && actor.Node.Active && head == 86, "native owned flags, active scheduler and actual tile head");
            var membership = new WorldCreatureSectorMembership();
            membership.RegisterAmbient(actor, row.GetProperty("interest_flags").GetUInt32());
            Check(membership.Get(actor.Base.Sector).SequenceEqual(row.GetProperty("members").EnumerateArray().Select(value=>value.GetUInt32())) &&
                row.GetProperty("changed_tiles").GetArrayLength() == 1, "native tile insertion and actual runtime-gated sector membership");
            Check(random.State == row.GetProperty("rng_after").GetUInt32() && references.Available == 0 && counter.Value == 70 && ReferenceEquals(factory.Actors[86], actor), "same factory, reservation, counter and exact RNG");
            var activationCount = factory.Activation.ActivationCount;
            var activationRecords = factory.Activation.Records.Count;
            var plan = WorldCreatureSectorActivation.Prepare(actor.Base.Sector, SacredSectorCoordinates.ToGrid(keyx), factory, membership, null, null);
            WorldCreatureSectorActivation.Apply(factory, plan, null, double.NaN);
            Check(plan.Count == row.GetProperty("members").GetArrayLength() && factory.Activation.ActivationCount == activationCount &&
                factory.Activation.Records.Count == activationRecords && random.State == row.GetProperty("rng_after").GetUInt32(), "actual native member/factory position checks and already-active completion");
            if (plan.Count != 0)
            {
                var flags = actor.Node.Flags; actor.Node.Flags &= 0x7f;
                try
                {
                    var dormantPlan = WorldCreatureSectorActivation.Prepare(actor.Base.Sector, SacredSectorCoordinates.ToGrid(keyx), factory, membership, null, null);
                    try { WorldCreatureSectorActivation.Apply(factory, dormantPlan, null, double.NaN); throw new Exception("Missing scheduler clock bypassed."); }
                    catch (NotSupportedException) { checks++; }
                }
                finally { actor.Node.Flags = flags; }
            }
        }
        return checks;
        JsonDocument Read(string name) => HostSectorInitializationChecks.Read(root, name);
        void Check(bool value, string reason) { if (!value) throw new Exception("Host ambient factory: " + reason); checks++; }
    }
    internal static byte[] WireExample(string root)
    {
        using var proof = HostSectorInitializationChecks.Read(root, "native-host-ambient-factory");
        var body = Bytes(proof.RootElement.GetProperty("cases")[0].GetProperty("publications")[0], "payload_hex");
        body.AsSpan(GoldPhysicalStateLayout.SelectedVectorOffset, 12).Clear();
        return GoldObjectStateCodec.Create(0x16f, 1, body).Payload;
    }
    private static byte[] Bytes(JsonElement row, string name) => HostSectorInitializationChecks.Bytes(row, name);
}
