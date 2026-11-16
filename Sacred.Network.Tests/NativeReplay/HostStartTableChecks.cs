using System.Text.Json;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.Network.Gameplay;

internal static class HostStartTableChecks
{
    public static int Run(string root)
    {
        using var proof = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-host-start-table.json")));
        var data = Convert.FromHexString(proof.RootElement.GetProperty("file_hex").GetString()!);
        var checks = 0;
        foreach (var row in proof.RootElement.GetProperty("cases").EnumerateArray())
        {
            var archive = SacredMultiplayerStartArchive.FromPayload(row.GetProperty("present").GetBoolean() ? data : []);
            var flags = row.GetProperty("flags").GetUInt32();
            var selected = archive.Select(row.GetProperty("slot").GetByte(), GoldSessionGameFlagsCodec.IsCampaignGame(flags), row.GetProperty("level").GetUInt16());
            var expected = Convert.FromHexString(row.GetProperty("selected_hex").GetString()!);
            var native = System.Runtime.InteropServices.MemoryMarshal.Read<SacredMultiplayerStartPositionLayout>(expected);
            Check(selected.Sector == native.Sector && selected.X == native.X && selected.Y == native.Y && selected.Surface == native.Surface,
                "constructor-loaded position selected using actual campaign predicate/level-mode gate");
        }
        Check(data.Length == SacredMultiplayerStartPositionLayout.FileSize, "original table size");
        // The final table is retained as original bytes; its purpose is not inferred.
        Check(SacredMultiplayerStartArchive.FromPayload(data).Data.Span.SequenceEqual(data), "all original records and alignment bytes preserved");
        return checks;
        void Check(bool pass, string reason) { if (!pass) throw new InvalidDataException("HostStartTable: " + reason); checks++; }
    }
}
