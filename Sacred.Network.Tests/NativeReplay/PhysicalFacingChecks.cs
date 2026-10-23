using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Network.Gameplay;

internal static class PhysicalFacingChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-physical-facing.json")));
        foreach (var sample in document.RootElement.GetProperty("samples").EnumerateArray())
        {
            var vector = sample.GetProperty("vector").EnumerateArray().Select(value => value.GetSingle()).ToArray();
            var bits = GoldPhysicalFacing.FromDirection(new(vector[0], vector[1], vector[2]));
            var expected = sample.GetProperty("degrees").GetSingle();
            if (MathF.Abs(BitConverter.UInt32BitsToSingle(bits) - expected) > 0.00003f)
                throw new Exception("Physical heading differs from complete native projection/atan2.");
            checks++;
        }
        // A full state must replace stale end-facing metadata with its physical vector, not cached base +0x28.
        var physical = new byte[GoldPhysicalStateLayout.Size];
        new GoldObjectBaseState(1, 100, 0, 1, 0, 0, 0, 0, 0, 0, 1, 0).ToPayload().CopyTo(physical, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(physical.AsSpan(GoldPhysicalStateLayout.MarkerOffset), GoldPhysicalStateLayout.Marker);
        BinaryPrimitives.WriteSingleLittleEndian(physical.AsSpan(GoldPhysicalStateLayout.Vector70Offset), -1);
        BinaryPrimitives.WriteSingleLittleEndian(physical.AsSpan(GoldPhysicalStateLayout.Vector70Offset + 4), -0.5f);
        var world = new GoldWorldReplica();
        world.Apply(GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16E, 1, physical)));
        var facing = world.Objects[1].FacingBits;
        if (facing == 0 || world.Objects[1].BaseState.Word28 != 0) throw new Exception("Full-state heading lost its physical source or changed cached base bytes.");
        world.Apply(GoldGameEventDecoder.Decode(GoldMovementPacketCodec.CreateEnd(1, 1, new(0, 0, 0, 1), 0, 0)));
        world.Apply(GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16E, 1, physical)));
        if (world.Objects[1].FacingBits != facing) throw new Exception("Full state retained obsolete stop facing.");
        checks += 2;
        Console.WriteLine($"PASS: {checks} physical heading checks against complete native calculations and full-state replacement.");
        return checks;
    }
}
