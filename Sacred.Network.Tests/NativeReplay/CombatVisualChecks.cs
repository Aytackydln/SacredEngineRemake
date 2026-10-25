using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;
using Sacred.Network.Gameplay;
using Sacred.Particles;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

internal static class CombatVisualChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool pass, string why) { if (!pass) throw new Exception("Combat visuals: " + why); checks++; }
        using var evidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-combat-visuals.json")));
        var tuning = new SacredCreatureMarkerBalance(0, 0, .85f, 1.1f, 1);
        foreach (var row in evidence.RootElement.GetProperty("deflection_initializations").EnumerateArray())
        {
            var samples = new Queue<int>(row.GetProperty("samples").EnumerateArray().Select(v => v.GetInt32()));
            var result = SacredDeflectionFxPlayback.Initialize(SacredDeflectionFxDefinition.Original with { StreakCount = 1 }, samples.Dequeue);
            var actual = result.Positions.Concat(result.Velocities).SelectMany(v => new[] { v.X, v.Y, v.Z }).ToArray();
            var expected = row.GetProperty("positions").EnumerateArray().Concat(row.GetProperty("velocities").EnumerateArray())
                .SelectMany(v => v.EnumerateArray()).Select(v => BitConverter.UInt32BitsToSingle(v.GetUInt32())).ToArray();
            for (var i = 0; i < actual.Length; i++) Check(Math.Abs(actual[i] - expected[i]) < .0001f, "native deflection initialization and trail order");
        }
        foreach (var row in evidence.RootElement.GetProperty("deflections").EnumerateArray())
        {
            Vector3 Vector(string key) { var a = row.GetProperty(key).EnumerateArray().Select(v => v.GetSingle()).ToArray(); return new(a[0], a[1], a[2]); }
            float Float(string key) => BitConverter.UInt32BitsToSingle(row.GetProperty(key).GetUInt32());
            var result = SacredDeflectionFxPlayback.AdvancePoint(Vector("position"), Vector("velocity"),
                Float("life_bits"), Float("delta_bits"), SacredDeflectionFxDefinition.Original);
            var actual = new[] { result.Position.X, result.Position.Y, result.Position.Z, result.Velocity.X, result.Velocity.Y, result.Velocity.Z, result.Life };
            var expected = row.GetProperty("result_position").EnumerateArray().Concat(row.GetProperty("result_velocity").EnumerateArray())
                .Select(v => BitConverter.UInt32BitsToSingle(v.GetUInt32())).Append(Float("result_life")).ToArray();
            for (var i = 0; i < actual.Length; i++) Check(Math.Abs(actual[i] - expected[i]) <= .00005f, "native deflection integration/damping/life");
        }
        foreach (var row in evidence.RootElement.GetProperty("flare_colors").EnumerateArray())
            Check(SacredHitFlashFxPlayback.ColorAt(BitConverter.UInt32BitsToSingle(row.GetProperty("weight_bits").GetUInt32()), 255) ==
                row.GetProperty("color").GetUInt32(), "native extended-precision flash alpha truncation");
        foreach (var row in evidence.RootElement.GetProperty("colors").EnumerateArray())
            Check(tuning.LevelColor(row.GetProperty("level").GetInt32(), row.GetProperty("hero_level").GetInt32()) ==
                row.GetProperty("color").GetUInt32(), $"native health colour, level {row.GetProperty("level")}, hero {row.GetProperty("hero_level")}");
        var vitality = MemoryMarshal.Read<SacredSkillCurveLayout>(new byte[] { 0, 0, 0, 0, 0, 0, 128, 63, 0, 0, 128, 63 });
        var balance = new SacredCreatureBalance(Enumerable.Range(1, 5).Select(i => new SacredCreatureHealthLevel(i, 1)),
            Enumerable.Repeat(1f, 6), vitality,
            evidence.RootElement.GetProperty("level_percentages").EnumerateArray().Select(v => v.GetInt32()),
            evidence.RootElement.GetProperty("level_additions").EnumerateArray().Select(v => v.GetInt32()));
        foreach (var row in evidence.RootElement.GetProperty("color_context").EnumerateArray())
        {
            var level = SacredCreatureProgressionRules.BaseLevelForDifficulty(row.GetProperty("level").GetUInt32(),
                row.GetProperty("difficulty").GetUInt32(), balance);
            Check(tuning.LevelColor((int)level, row.GetProperty("hero_level").GetInt32()) == row.GetProperty("color").GetUInt32(),
                $"native full health colour context: {row}");
        }
        foreach (var row in evidence.RootElement.GetProperty("hit_reactions").EnumerateArray())
        {
            var world = new GoldWorldReplica(); var actor = (GoldObjectEvent)Actor(); var bytes = actor.ObjectState.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldObjectStateLayout.FlagsOffset), row.GetProperty("base_flags").GetUInt32());
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.Word200Offset), row.GetProperty("flags").GetUInt32());
            world.Apply(GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16B, 1, bytes)));
            world.Apply(Event(0x128, Words(500, 100))); world.Apply(Event(0x17A, Words(500, 1, 0, 2, 0, 0)));
            var control = row.GetProperty("control");
            Check(GoldCreatureHitReactionRules.ShouldPlay(world.Combat.Recent.Last()) == (control.ValueKind != JsonValueKind.Null),
                "native ordinary hit reaction flag gates");
            if (control.ValueKind == JsonValueKind.Null) continue;
            Check(SacredCreatureHitReaction.Motion == control.GetProperty("motion").GetUInt32(), "native hit motion");
            Check(BitConverter.SingleToUInt32Bits(SacredCreatureHitReaction.Speed) == control.GetProperty("speed_bits").GetUInt32(), "native hit speed");
            Check(BitConverter.SingleToUInt32Bits(SacredCreatureHitReaction.Weight(row.GetProperty("hero").GetBoolean() ? SacredCreatureClass.Hero : SacredCreatureClass.Monster)) ==
                control.GetProperty("weight_bits").GetUInt32(), "native hit control weight");
        }
        foreach (var row in evidence.RootElement.GetProperty("blood").EnumerateArray())
        {
            var bytes = new byte[128]; bytes[0x2E] = row.GetProperty("category").GetByte(); bytes[0x6F] = row.GetProperty("kind").GetByte();
            var item = MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(bytes);
            Check(SacredCreatureBlood.Color(item) == row.GetProperty("color").GetUInt32(), "native Items.pak blood palette");
        }
        foreach (var row in evidence.RootElement.GetProperty("aging").EnumerateArray())
        {
            float Float(string key) => BitConverter.UInt32BitsToSingle(row.GetProperty(key).GetUInt32());
            var result = SacredHitFlashFxPlayback.AdvanceState(Float("age_bits"), Float("weight_bits"), Float("delta_bits"), .75f);
            Check(BitConverter.SingleToUInt32Bits(result.Age) == row.GetProperty("result_age_bits").GetUInt32(), "native flash age");
            Check(BitConverter.SingleToUInt32Bits(result.Weight) == row.GetProperty("result_weight_bits").GetUInt32(), "native flash weight");
            Check(result.Active == row.GetProperty("active").GetBoolean(), "native strict-negative expiry");
        }
        return checks;
    }
}
