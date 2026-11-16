using System.Text.Json;
using Sacred.Core.Pax.Hero.Objects;

internal static class HostHeroAnimationRequestChecks
{
    public static int Run(string root)
    {
        using var proof = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-host-hero-animation-request.json")));
        var checks = 0;
        foreach (var row in proof.RootElement.GetProperty("cases").EnumerateArray())
        {
            var request = SacredSavedAnimationRequest.Read(Convert.FromHexString(row.GetProperty("source_hex").GetString()!));
            var calls = row.GetProperty("requests").EnumerateArray().ToArray();
            Check(request.RequiresActivation == (calls.Length != 0), "nonzero selector requests activation");
            Check(calls.Length == (request.RequiresActivation ? row.GetProperty("activation_result").GetInt32() != 0 ? 2 : 1 : 0), "animation follows successful activation only");
            if (calls.Length == 2)
            {
                var args = calls[1].EnumerateArray().ToArray();
                Check(request.Selection == args[1].GetUInt32(), "original selector");
                Check(BitConverter.SingleToUInt32Bits(request.FirstArgument) == args[2].GetUInt32(), "first narrowed double");
                Check(BitConverter.SingleToUInt32Bits(request.SecondArgument) == args[3].GetUInt32(), "second narrowed double");
            }
        }
        return checks;
        void Check(bool pass, string reason) { if (!pass) throw new InvalidDataException("HostHeroAnimationRequest: " + reason); checks++; }
    }
}
