using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin.Balance;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class HostAutomaticLevelSourceChecks
{
    public static int Run(string root)
    {
        using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-host-automatic-level-source.json")));
        using var tuning = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-script-automatic-level.json")));
        var data = source.RootElement; var compared = tuning.RootElement;
        var image = new byte[compared.GetProperty("image_size").GetInt32()];
        foreach (var row in compared.GetProperty("definition_chunks").EnumerateArray())
            Bytes(row, "hex").CopyTo(image, row.GetProperty("offset").GetInt32());
        var balance = Bytes(compared, "balance_hex");
        var teams = new GoldTeamReplica();
        if (!teams.Slots.SequenceEqual(Bytes(data,"team_hex").AsSpan(0,64).ToArray().Chunk(4).Select(bytes => BitConverter.ToUInt32(bytes))))
            throw new Exception("Original cold team state mismatch.");
        var scratch = Bytes(data, "scratch_hex");
        var inputs = new WorldCreatureAutomaticLevelInputs(data.GetProperty("hero_level").GetUInt16(), [],
            data.GetProperty("statistics_elapsed").GetUInt32(), scratch[28], data.GetProperty("skill22").GetByte(), false, false, 0, 0);
        var selected = WorldCreatureAutomaticLevel.Select(inputs,1,true,false,
            CreatureLevelBalanceBinReader.Read(balance),CreatureBalanceBinReader.Read(balance),
            SacredCreatureAutomaticLevelDefinitions.Read(image).Tuning,SacredX87Precision.Single,
            () => throw new Exception("The unloaded native source consumes no clamp draw."));
        if (selected.RequestedLevel != data.GetProperty("level_calls")[0][0].GetUInt32())
            throw new Exception("Entered host native automatic request mismatch.");
        return 2;
    }
    private static byte[] Bytes(JsonElement row,string key) => Convert.FromHexString(row.GetProperty(key).GetString()!);
}
