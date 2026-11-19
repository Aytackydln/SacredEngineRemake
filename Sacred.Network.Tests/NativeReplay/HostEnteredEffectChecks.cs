using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class HostEnteredEffectChecks
{
    public static int Run(string root)
    {
        using var proof = Read(root);
        using var startup = HostSectorInitializationChecks.Read(root, "native-startup-effect-factory");
        var definitions = StartupEffectFactoryChecks.Definitions(startup.RootElement);
        var checks = 0;
        var command = Convert.FromHexString(proof.RootElement.GetProperty("command_hex").GetString()!);
        var creation = WorldScriptEnteredFixedEffect.Read(new(0x2aa68a, command));
        Check(creation.TypeId == definitions.Resource && creation.WorldPosition == new Sacred.Core.GameBin.Scripts.SacredScriptPosition(180289,133385,0),
            "reached literal world coordinates take precedence over tile-center placement");
        foreach (var row in proof.RootElement.GetProperty("cases").EnumerateArray())
        {
            var bytes = Convert.FromHexString(row.GetProperty("publications")[0].GetProperty("payload_hex").GetString()!);
            var native = GoldObjectFxState.FromPayload(bytes);
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var projected = WorldStartupEffectProjection.ComparedGold.Project(native.BaseState.X,native.BaseState.Y,0);
            var actual = WorldFixedEffectSerialization.Create(definitions,native.BaseState,projected,random).ToPayload();
            var canonical = bytes.ToArray();
            foreach (var (offset,length) in new[] { (71,12),(115,24),(268,3),(311,72),(417,12),(437,8) }) canonical.AsSpan(offset,length).Clear();
            Check(actual.AsSpan().SequenceEqual(canonical), "complete defined native entered Effect and particle stream");
            Check(random.State == row.GetProperty("rng_after").GetUInt32() && row.GetProperty("rng_calls").GetArrayLength() == 4,
                "exact four shared constructor/start RNG draws");
            var runtime = Convert.FromHexString(row.GetProperty("effect_hex").GetString()!);
            Check(runtime[8] == 0x81 && BinaryPrimitives.ReadUInt32LittleEndian(runtime.AsSpan(20)) == 0x90000000 && native.BaseState.Flags == 0x10000000,
                "active runtime node and post-publication local flag remain distinct from wire ownership");
            Check(native.BaseState.ObjectReference == 44 && native.BaseState.Sector == 2503 && native.BaseState.Byte38 == 28 && native.BaseState.Owner == 1 &&
                row.GetProperty("members")[0].GetUInt32() == 44 && row.GetProperty("tile_edits")[0].GetInt32() == 3423,
                "actual granted identity, shared counter, outdoor head and membership");
            Check(row.GetProperty("hero_queries").GetInt32() == 0 && row.GetProperty("publications").GetArrayLength() == 1,
                "no absent-hero branch or duplicate publication");
        }
        return checks;
        void Check(bool value,string reason) { if (!value) throw new Exception("Entered Effect: " + reason); checks++; }
    }
    public static byte[] WireExample(string root)
    {
        using var proof = Read(root);
        return GoldObjectStateCodec.Create(0x16f,1,Convert.FromHexString(proof.RootElement.GetProperty("cases")[0]
            .GetProperty("publications")[0].GetProperty("payload_hex").GetString()!)).Payload;
    }
    private static JsonDocument Read(string root) => HostSectorInitializationChecks.Read(root,"native-host-entered-effect");
}
