using System.Buffers.Binary;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Networking;

internal static class HostRestoreSourceChecks
{
    public static int Run(string root)
    {
        using var proof = HostSectorInitializationChecks.Read(root, "native-host-restore-source");
        var data = proof.RootElement; var checks = 0;
        Check(data.GetProperty("save_marker").GetUInt32() == SacredScriptSectorRestoreSaveLayout.Marker, "original importer marker");
        foreach (var row in data.GetProperty("cases").EnumerateArray())
        {
            var writes = row.GetProperty("writes");
            Check(BinaryPrimitives.ReadUInt32LittleEndian(Convert.FromHexString(writes[0].GetString()!)) == SacredScriptSectorRestoreSaveLayout.Marker,
                "original save marker precedes count and records");
            var count = BinaryPrimitives.ReadUInt32LittleEndian(Convert.FromHexString(writes[1].GetString()!));
            var records = Convert.FromHexString(writes[2].GetString()!);
            Check(count == row.GetProperty("count").GetUInt32() && records.Length == count * SacredScriptSectorRestoreSaveLayout.RecordSize,
                "original save count and stride multiplication");
            Check(records.SequenceEqual(HostSectorInitializationChecks.Bytes(row, "record_hex")), "native row storage copied without interpretation");
            Check(row.GetProperty("reset_count").GetInt32() == 0, "native reset erases existing rows without establishing production import selection");
        }
        foreach (var row in data.GetProperty("fresh_cases").EnumerateArray())
        {
            var mode = row.GetProperty("mode").GetUInt32();
            var loads = new WorldHostStartupRequest(1, mode).LoadsStartCode;
            Check(loads == row.GetProperty("loader_selected").GetBoolean(), "native Engine script-loader gate");
            Check(row.GetProperty("remaining_count").GetInt32() == (loads ? 0 : row.GetProperty("initial_count").GetInt32()),
                "only a selected script loader reaches the source reset");
            Check(row.GetProperty("capacity_bytes").GetInt32() == 8192, "reset retains capacity rather than manufacturing a new allocation");
        }
        var operation = WorldHostStartupRequest.FreshWorld;
        var source = WorldHostSectorRestoreSource.FromFreshWorld(operation);
        source.RequireEmptyFreshSource(operation);
        Check(source.Count == 0, "production source agrees with native fresh-loader reset");
        foreach (var row in data.GetProperty("visits").EnumerateArray())
            Check((row.GetProperty("count").GetInt32() == source.Count) == (row.GetProperty("next_address").GetUInt32() == 0x495472),
                "native empty-vector pass reaches final patches; positive controls reach record body");
        try { source.RequireEmptyFreshSource(WorldHostStartupRequest.FreshWorld); throw new Exception("Unrelated operation accepted."); }
        catch (InvalidOperationException) { checks++; }
        foreach (var request in new[] { new WorldHostStartupRequest(0, 1), new WorldHostStartupRequest(1, 0), new WorldHostStartupRequest(4, 0) })
        {
            try { _ = WorldHostSectorRestoreSource.FromFreshWorld(request); throw new Exception("Unverified operation accepted."); }
            catch (NotSupportedException) { checks++; }
        }
        return checks;
        void Check(bool value, string reason) { if (!value) throw new Exception("Host restore source: " + reason); checks++; }
    }
}
