using System.Buffers.Binary;
using System.Text.Json;
using Sacred.GameplayHost.World;
using Sacred.Network.Gameplay;
using Sacred.Network.Tests.Infrastructure;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

internal static class ConsumerPresentationChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool pass, string why) { if (!pass) throw new Exception("Consumer presentation: " + why); checks++; }
        using var evidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-consumer-presentation.json")));
        foreach (var row in evidence.RootElement.GetProperty("colors").EnumerateArray())
        {
            var reference = row.GetProperty("reference").GetUInt32(); var world = new GoldWorldReplica();
            var actor = (GoldObjectEvent)Actor(); var bytes = actor.ObjectState.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), reference);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.Word251Offset), row.GetProperty("link").GetUInt32());
            world.Apply(GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16B, 1, bytes)));
            world.Apply(Event(0x128, Words(reference, 100))); world.Apply(Event(0x17A, Words(reference, 10, 1, 2, 0, 0)));
            var texts = new GameplayReceivedTexts(world); texts.Advance(0);
            Check(texts.Pool.Entries.Single().ColorArgb == row.GetProperty("color").GetUInt32(), "ordinary damage color");
        }
        foreach (var row in evidence.RootElement.GetProperty("drag").EnumerateArray())
        {
            var world = new GoldWorldReplica(); world.Apply(Actor());
            var drag = new GameplayReceivedDragDrop(world); drag.Update();
            drag.Current = new(1, row.GetProperty("category").GetUInt16(), row.GetProperty("reference").GetUInt32(), 2);
            world.Apply(Event(0x13D, Words(500))); drag.Update();
            Check(drag.Current is null == row.GetProperty("cleared").GetBoolean(), "kind-1 zero-slot descriptor clears");
        }
        foreach (var row in evidence.RootElement.GetProperty("setups").EnumerateArray())
        {
            var notice = new GameplayHudNotice("Text", row.GetProperty("font").GetUInt32(), 1);
            Check(BitConverter.SingleToUInt32Bits(notice.RemainingAlpha) == row.GetProperty("alpha_bits").GetUInt32(), "initial alpha");
            Check(BitConverter.SingleToUInt32Bits(notice.Age) == row.GetProperty("age_bits").GetUInt32(), "initial age");
            Check(notice.ColorArgb == row.GetProperty("color").GetUInt32(), "mode-16 white color");
        }
        foreach (var row in evidence.RootElement.GetProperty("aging").EnumerateArray())
        {
            float Float(string key) => BitConverter.UInt32BitsToSingle(row.GetProperty(key).GetUInt32());
            var result = GameplayHudNotice.AdvanceState(Float("age_bits"), Float("alpha_bits"), Float("delta_bits"));
            Check(BitConverter.SingleToUInt32Bits(result.Age) == row.GetProperty("result_age_bits").GetUInt32(), "native stored age");
            Check(BitConverter.SingleToUInt32Bits(result.Alpha) == row.GetProperty("result_alpha_bits").GetUInt32(), "native full-tick alpha subtraction");
            Check((result.Age > 7 && result.Alpha <= 0) == row.GetProperty("expired").GetBoolean(), "native zero-or-negative expiry after hold");
        }
        foreach (var row in evidence.RootElement.GetProperty("particle_flags").EnumerateArray())
        {
            var world = new GoldWorldReplica();
            if (row.GetProperty("present").GetBoolean()) world.Apply(RemainingReceivePackets.FxObject());
            var previous = row.GetProperty("previous").GetUInt16();
            if (previous != 0) world.Apply(Event(previous == 2 ? (ushort)0x182 : (ushort)0x184, Words(500)));
            var flag = row.GetProperty("flag").GetUInt16();
            world.Apply(Event(flag == 2 ? (ushort)0x182 : (ushort)0x184, Words(500)));
            Check((world.Objects.GetValueOrDefault(500u)?.SpecialFx?.ParticleFlags ?? previous)
                == row.GetProperty("result").GetUInt16(), "native particle flag replacement");
        }
        return checks;
    }
}
