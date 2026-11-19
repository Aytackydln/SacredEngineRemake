using System.Buffers.Binary;
using System.Text.Json;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

internal static class TriggerPatchBankSourceChecks
{
    public static int Run(string root)
    {
        using var proof = Read(root);
        var checks = 0;
        foreach (var row in proof.RootElement.GetProperty("cases").EnumerateArray())
        {
            var pairs = Pairs(row);
            var expected = Convert.FromHexString(row.GetProperty("wire_hex").GetString()!);
            var bytes = WorldScriptTriggerPatchBank.Write(pairs);
            Check(bytes.SequenceEqual(expected),"actual native AVL order and whole section bytes");
            Check(bytes.Length == row.GetProperty("count").GetInt32() * 8,"native section count");
            Array.Fill(bytes,(byte)0xa5);
            Check(WorldScriptTriggerPatchBank.Write(pairs).SequenceEqual(expected),"independent output ownership");
        }
        var world = new GoldWorldReplica();
        world.Apply(GoldGameEventDecoder.Decode(BankThreeWireExample(root)));
        var sample = Pairs(proof.RootElement.GetProperty("cases")[2]);
        Check(sample.All(pair => world.WorldBanks.TriggerPatches.Mappings[pair.Key] == pair.Value),"shared bank decoder/import keeps unsigned packed cells and references");
        Check(world.Objects.Count == 0,"patch sources do not manufacture actors");
        return checks;
        void Check(bool condition,string reason) { if(!condition)throw new Exception("Trigger-patch bank: "+reason);checks++; }
    }
    internal static GoldMessage BankThreeWireExample(string root)
    {
        using var proof = Read(root);
        var pairs = Pairs(proof.RootElement.GetProperty("cases")[2]);
        var body = WorldScriptTriggerPatchBank.Write(pairs);
        // Contract fixture header only; production still requires every other bank source.
        var payload = new byte[GoldWorldBankLayout.HeaderSize + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8),(uint)pairs.Count);
        body.CopyTo(payload,GoldWorldBankLayout.HeaderSize);
        return new(0x1b3,0,1,0,payload);
    }
    private static Dictionary<uint,uint> Pairs(JsonElement row) => row.GetProperty("pairs").EnumerateArray()
        .ToDictionary(pair => pair[0].GetUInt32(),pair => pair[1].GetUInt32());
    private static JsonDocument Read(string root) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-trigger-patch-bank-source.json")));
}
