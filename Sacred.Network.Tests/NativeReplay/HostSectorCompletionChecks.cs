using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Core.World.Sector;
using Sacred.GameplayHost.Scripting;
using Sacred.GameplayHost.World;
using Sacred.Network.Gameplay;

internal static class HostSectorCompletionChecks
{
    public static int Run(string root)
    {
        using var proof = HostSectorInitializationChecks.Read(root, "native-host-sector-completion");
        var checks = 0;
        foreach (var row in proof.RootElement.GetProperty("weather").EnumerateArray())
        {
            var environment = MemoryMarshal.Read<SectorEnvironmentLayout>(HostSectorInitializationChecks.Bytes(row, "environment_hex"));
            var required = WorldSectorEntryWeather.RequiresCreatureCreation(environment);
            Check(required == row.GetProperty("effect_required").GetBoolean(), "whole native weather handler's nonzero mask/all-four-entry gate");
            if (required)
            {
                try { WorldSectorEntryWeather.Enter(environment); throw new Exception("Unsupported weather factory was bypassed."); }
                catch (NotSupportedException) { checks++; }
            }
            else { WorldSectorEntryWeather.Enter(environment); Check(row.GetProperty("returned").GetBoolean(), "native complete handler returns without mutation or forwarding"); }
        }
        foreach (var row in proof.RootElement.GetProperty("tails").EnumerateArray())
        {
            var before = row.GetProperty("flags_before").GetUInt32();
            Check((before | GoldSectorEntryRuntime.EnteredFlag) == row.GetProperty("flags_after").GetUInt32(), "OR DH,8 reasserts 0x800");
            var required = row.GetProperty("activation_required").GetBoolean();
            Check(row.GetProperty("returned").GetBoolean() != required, "inactive control reaches the actual activation dependency");
            if (required) continue;
            var queue = new WorldObjectActivationQueue();
            foreach (var value in row.GetProperty("actor_hex").EnumerateArray())
            {
                var bytes = Convert.FromHexString(value.GetString()!);
                var actor = new WorldCreatureInitialActor(new(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12)), 0, 0, 2502, 0, 0, 0, 0, 0, 0, 1, 0),
                    new([], [], 0, 0), 0, 0, 0, 0, 0, 0, []);
                actor.Node.Flags = bytes[8];
                Check(!queue.ActivateInitial(actor, double.NaN) && actor.Node.Flags == bytes[8], "640640 active branch ignores clock/identity and has no callback");
            }
            Check(queue.ActivationCount == 0 && queue.Nodes.Count == 0 && queue.Records.Count == 0, "no new scheduler records for existing active members");
        }
        return checks;
        void Check(bool value, string reason) { if (!value) throw new Exception("Host sector completion: " + reason); checks++; }
    }
}
