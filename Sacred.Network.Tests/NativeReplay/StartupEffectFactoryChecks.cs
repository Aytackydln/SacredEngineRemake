using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Core.Runtime;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Particles.Particles;
using Sacred.Assets.Executable;
using Sacred.GameplayHost.Scripting;

internal static class StartupEffectFactoryChecks
{
    public static int Run(string root)
    {
        using var fixture = Read(root);
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var definitions = Definitions(fixture.RootElement);
        Check(definitions.Resource == 802 && definitions.EventCode == 34 && definitions.AngleBits == 0x3f490fdb &&
            definitions.SelectionMask == 15 && definitions.SelectionMaximum == 15 && definitions.ParticleCapacity == 200 &&
            definitions.NodePeriod == 25 && definitions.BaseFlags == 2 && definitions.RechargeBits == 0x3a83126f && definitions.WordE0Bits == 0x40400000,
            "Original executable supplies resource/family selection and all fixed Effect initialization operands.");
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var creation = row.GetProperty("mode").GetString() == "creation";
            var first = (uint)(random.Next() % 16); var second = (uint)(random.Next() % 16);
            if (creation) { first = (uint)(random.Next() % 16); second = (uint)(random.Next() % 16); }
            Check(random.State == row.GetProperty("rng_after").GetUInt32() &&
                row.GetProperty("rng_calls").GetArrayLength() == (creation ? 4 : 2), "Original particle constructor/start consume exactly the shared RNG draws.");
            var particle = Convert.FromHexString(row.GetProperty("particle_hex").GetString()!);
            var state = MemoryMarshal.Read<SacredStargateParticleStateLayout>(particle.AsSpan(0x20a0));
            Check(state.Started == 1 && state.MinimapSlot0 == first && state.MinimapSlot1 == second &&
                BitConverter.SingleToUInt32Bits(state.Angle0) == 0x3f490fdb && state.Angle0 == state.Angle1,
                "Native state mapping retains the current minimap selections and angle initialization.");
            Check(row.GetProperty("factories").GetArrayLength() == 1 && row.GetProperty("activations").GetArrayLength() == 1 &&
                row.GetProperty("textures").GetArrayLength() == 18, "Original factory creates one effect and one active particle system with original texture-name lookups.");
            if (!creation) continue;
            var publication = row.GetProperty("publications").EnumerateArray().Single();
            var bytes = Convert.FromHexString(publication.GetProperty("payload_hex").GetString()!);
            var fx = GoldObjectFxState.FromPayload(bytes);
            Check(fx.BaseState.ObjectReference == 44 && fx.BaseState.ObjectType == 802 && fx.FxType == 34 && fx.IsSystemActive &&
                fx.BaseState.X == 670 && fx.BaseState.Y == 992 && fx.BaseState.Byte38 == 28,
                "Native CreateObj placement, shared counter and FX event fields.");
            var snapshot = GoldParticleSnapshot.FromPayload(fx.ParticleState.Span);
            var projected = WorldStartupEffectProjection.ComparedGold.Project(fx.BaseState.X, fx.BaseState.Y, 0);
            Check(projected == snapshot.Position, "Shared projection matches the original initialized renderer context exactly.");
            var producedRandom = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var produced = WorldFixedEffectSerialization.Create(definitions, fx.BaseState, snapshot.Position, producedRandom).ToPayload();
            var canonical = bytes.ToArray();
            foreach (var (offset, length) in new[] { (71,12),(115,24),(268,3),(311,72),(417,12),(437,8) }) canonical.AsSpan(offset,length).Clear();
            Check(produced.AsSpan().SequenceEqual(canonical) && producedRandom.State == row.GetProperty("rng_after").GetUInt32(),
                "Compared initial fixed Effect writer matches every defined native byte, complete particle state and shared RNG.");
            Check(snapshot.DerivedState.Span.SequenceEqual(particle.AsSpan(0x20a0, 0x34)) && snapshot.Resource == 802 &&
                float.IsFinite(snapshot.Position.X) && float.IsFinite(snapshot.Position.Y) && snapshot.BaseState.Length == 160,
                "Actual full FX stream contains the derived particle state and finite initialized base position.");
            Check(snapshot.ToPayload().AsSpan().SequenceEqual(fx.ParticleState.Span), "Complete unknown particle bytes remain lossless.");
            var copy = snapshot.ToPayload(); copy.AsSpan().Fill(0);
            Check(snapshot.Resource == 802, "Returned snapshot bytes cannot modify retained state.");
            var wire = GoldObjectStateCodec.Create(0x16f, 1, bytes);
            Check(wire.Payload.AsSpan(0, 24).SequenceEqual(Convert.FromHexString(publication.GetProperty("prefix_hex").GetString()!)),
                "Original complete FX publication prefix.");
            var decoded = (GoldObjectEvent)GoldGameEventDecoder.Decode(wire);
            Check(decoded.ObjectState.AsSpan().SequenceEqual(bytes), "Existing decoder retains complete active particle state.");
            foreach (var index in new[] { 0, 4, 60, 64 })
            {
                var invalid = snapshot.ToPayload(); BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(index), uint.MaxValue);
                try { GoldParticleSnapshot.FromPayload(invalid); throw new Exception("Invalid particle framing accepted."); }
                catch (InvalidDataException) { checks++; }
            }
        }
        return checks;
    }
    public static byte[] WireExample(string root)
    {
        using var fixture = Read(root);
        var row = fixture.RootElement.GetProperty("cases").EnumerateArray().First(value =>
            value.GetProperty("mode").GetString() == "creation" && value.GetProperty("fill").GetInt32() == 0 && value.GetProperty("seed").GetUInt32() == 2004);
        var bytes = Convert.FromHexString(row.GetProperty("publications")[0].GetProperty("payload_hex").GetString()!);
        return GoldObjectStateCodec.Create(0x16f, 1, bytes).Payload;
    }
    private static JsonDocument Read(string root) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
        "docs/game-server/evidence/native-startup-effect-factory.json")));
    public static SacredFixedEffectDefinitions Definitions(JsonElement fixture)
    {
        var segments = fixture.GetProperty("definition_code").EnumerateArray().ToArray();
        var image = new byte[segments.Max(row => row.GetProperty("offset").GetInt32() + row.GetProperty("hex").GetString()!.Length / 2)];
        foreach (var segment in segments) Convert.FromHexString(segment.GetProperty("hex").GetString()!).CopyTo(image, segment.GetProperty("offset").GetInt32());
        return SacredFixedEffectDefinitions.Read(image);
    }
}
