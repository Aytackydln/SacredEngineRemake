using System.Text.Json;
using Sacred.Core.CombatArts;
using Sacred.GameplayHost.World;
using Sacred.Network.Gameplay;
using Sacred.Network.Tests.Infrastructure;
using Sacred.Particles;

internal static class SeraphimVisualChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool condition, string detail) { if (!condition) throw new Exception(detail); checks++; }
        using var evidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-seraphim-visual-replication.json")));
        foreach (var sample in evidence.RootElement.GetProperty("samples").EnumerateArray())
        {
            var code = sample.GetProperty("spell").GetUInt32();
            var targetAvailable = sample.GetProperty("targetAvailable").GetBoolean();
            var definition = SacredCombatVisualCatalogue.Find(SacredCombatArtKind.Spell, code).Single();
            Check(definition.Resource == sample.GetProperty("resource").GetUInt32(), "Native resource " + code);
            Check(definition.FxSubtype == sample.GetProperty("subtype").GetUInt32(), "Native event subtype " + code);
            Check(definition.Duration(1) == sample.GetProperty("durationRankOne").GetSingle(), "Native rank-one duration " + code);
            var wearer = sample.GetProperty("wearer").GetUInt32();
            Check((wearer == 0) == (definition.Bone.Length == 0), "Native fixed-ground attachment " + code);
            if (wearer != 0)
            {
                Check(definition.Bone == SacredFxAttachments.BoneName(sample.GetProperty("bone").GetUInt16()), "Native wearer bone " + code);
                Check(definition.TargetIsCaster || targetAvailable || definition.FallbackToCaster, "Native missing-target fallback " + code);
            }
            var world = new GoldWorldReplica();
            world.Apply(GoldGameEventDecoder.Decode(SeraphimVisualPackets.Creature(500)));
            if (targetAvailable) world.Apply(GoldGameEventDecoder.Decode(SeraphimVisualPackets.Creature(501)));
            world.Apply(GoldGameEventDecoder.Decode(SeraphimVisualPackets.Cast(code, targetAvailable ? 501u : 0)));
            var effects = new GameplayStatusEffects(world, _ => { });
            effects.Advance(0); effects.SetActionTiming(world.Objects[500], 2); effects.Advance(2);
            var effect = effects.Entries.Values.Single();
            Check(effect.VisualAvailable, "Native visual is available " + code);
            Check(effect.Owner == (wearer == 0 ? 0u : wearer == 42 ? 500u : 501u), "Managed owner matches native factory/dispatch " + code);
        }
        return checks;
    }
}
