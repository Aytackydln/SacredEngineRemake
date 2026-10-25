using System.Text.Json;
using Sacred.Core.Pak.Creature;
using Sacred.Network.Gameplay;
using Sacred.Network.Tests.Infrastructure;
using Sacred.Particles;
using Sacred.World.Networking;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

internal static class CreatureDeathChecks
{
    public static int Run(string root)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-creature-death.json")));
        var checks = 0;
        void Check(bool value, string why) { if (!value) throw new Exception("Creature death: " + why); checks++; }
        foreach (var entry in document.RootElement.GetProperty("entries").EnumerateArray())
        {
            var world = new GoldWorldReplica(); world.Apply(CreatureCommandPackets.Actor(500));
            world.Apply(Event(0x17D,Words(500,entry.GetProperty("player").GetUInt32())));
            var actor = world.Objects[500];
            Check(actor.CurrentHitPoints == entry.GetProperty("hp").GetInt32(),"native event 208 HP store");
            Check(actor.Death!.PlayerWord == entry.GetProperty("player").GetUInt32(),"death player operand remains distinct from killer reference");
            Check(actor.IsDead && actor.DeathVersion == 1 && (actor.BaseState.Flags & 0x10)==0,"death retains the active corpse lifetime");
        }
        foreach (var entry in document.RootElement.GetProperty("motions").EnumerateArray())
        {
            var available = entry.GetProperty("available").EnumerateArray().Select(v=>v.GetUInt32()).ToHashSet();
            var random = new Queue<int>(entry.GetProperty("samples").EnumerateArray().Select(v=>v.GetInt32()));
            Check(SacredCreatureDeathMotion.Select(0,available.Contains,random.Dequeue)==entry.GetProperty("slot").GetUInt32(),
                "native ordinary/alternate/special death availability and RNG");
        }
        foreach (var entry in document.RootElement.GetProperty("delays").EnumerateArray())
            Check((entry.GetProperty("seconds").GetSingle()>SacredNetworkCorpseRules.CleanupDelay(
                (SacredParticleQuality)entry.GetProperty("quality").GetByte()))==entry.GetProperty("cleanup").GetBoolean(),
                "native detail-level corpse cleanup boundary");
        return checks;
    }
}
