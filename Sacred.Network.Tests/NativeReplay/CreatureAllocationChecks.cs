using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class CreatureAllocationChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-allocation.json")));
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var previous = row.GetProperty("previous").GetByte();
            var counter = new WorldObjectAllocationCounter(previous);
            var activation = new WorldObjectActivationQueue();
            var events = new List<string>();
            var reference = row.GetProperty("reference").GetUInt32();
            var registry = new Dictionary<uint, WorldCreatureInitialActor>();
            var failed = row.GetProperty("failed").GetBoolean();
            var actor = WorldCreatureAllocation.Allocate(reference, counter, activation, reserved =>
            {
                if (reserved != reference || counter.Value != unchecked((byte)(previous + 1)) || registry.Count != 0)
                    throw new Exception("Native counter/reservation-before-constructor order differs.");
                events.Add("construct");
                for (var i = 0; i < row.GetProperty("nested").GetInt32(); i++) counter.BeginAllocation();
                return failed ? null : new(new(0, 295, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0),
                    new(new byte[SacredActorValuesLayout.Size], new byte[GoldCreatureValuesLayout.Block128Size], 6, 0),
                    7, 0, 0, 0, 0, 0, []);
            }, (reserved, initial) =>
            {
                if (initial.Node.Active || initial.Base.ObjectReference != 0 || initial.Base.Owner != 0 || initial.Base.Byte38 != counter.Value)
                    throw new Exception("Native registry-before-activation state differs.");
                registry.Add(reserved, initial); events.Add("register");
            }, row.GetProperty("clock").GetDouble());
            if (actor is not null) events.Add("activate");
            if (counter.Value != row.GetProperty("counter").GetByte() ||
                !events.SequenceEqual(row.GetProperty("events").EnumerateArray().Select(value => value.GetString()!)))
                throw new Exception("Native allocation counter/order differs.");
            checks += 6;
            if (failed)
            {
                if (actor is not null || registry.Count != 0 || activation.Nodes.Count != 0 || activation.Records.Count != 0 || activation.ActivationCount != 0)
                    throw new Exception("Failed constructor registered/activated an actor.");
                checks += 5;
                continue;
            }
            var record = Convert.FromHexString(row.GetProperty("record_hex").GetString()!);
            if (actor is null || !ReferenceEquals(registry[reference], actor) || actor.Base.Byte38 != counter.Value ||
                actor.Base.ObjectReference != 0 || actor.Base.Owner != 0 || actor.Node.Flags != row.GetProperty("node_flags").GetByte() ||
                activation.ActivationCount != 1 || activation.Nodes.Count != 1 || activation.Records.Count != 1 ||
                BitConverter.DoubleToUInt64Bits(actor.Node.StartedAt) != BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(8)) ||
                BitConverter.DoubleToUInt64Bits(actor.Node.NextDue) != BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(16)))
                throw new Exception("Original allocated actor/registry/node state differs.");
            checks += 11;
        }
        return checks;
    }
}
