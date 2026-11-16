using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Runtime;

internal static class CreatureResultFinalizationChecks
{
    public static int Run(string root)
    {
        // An asymmetric original item contribution must retain native maximum/
        // minimum field names for every consumer of the serialized actor block.
        foreach (var (name, offset) in new[] { (nameof(SacredActorValuesLayout.PhysicalDamageMaximum), 110),
            (nameof(SacredActorValuesLayout.FireDamageMaximum), 114), (nameof(SacredActorValuesLayout.MagicDamageMaximum), 118),
            (nameof(SacredActorValuesLayout.PoisonDamageMaximum), 122), (nameof(SacredActorValuesLayout.PhysicalDamageMinimum), 126),
            (nameof(SacredActorValuesLayout.FireDamageMinimum), 130), (nameof(SacredActorValuesLayout.MagicDamageMinimum), 134),
            (nameof(SacredActorValuesLayout.PoisonDamageMinimum), 138) })
            if (Marshal.OffsetOf<SacredActorValuesLayout>(name).ToInt32() != offset) throw new Exception($"Original actor damage layout differs: {name}.");
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-result-finalization.json")));
        var json = fixture.RootElement;
        var definitions = json.GetProperty("definition_variants").EnumerateArray().Append(json).Select(row =>
        {
            var image = new byte[row.GetProperty("image_size").GetInt32()];
            foreach (var chunk in row.GetProperty("definition_chunks").EnumerateArray())
                Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
            return SacredCreatureResultFinalizationDefinitions.Read(image);
        }).ToArray();
        var checks = 8;
        foreach (var row in json.GetProperty("cases").EnumerateArray())
            foreach (var rules in definitions)
            {
                var values = Convert.FromHexString(row.GetProperty("before_hex").GetString()!);
                var resource = row.GetProperty("resource").GetUInt32();
                SacredCreatureResultFinalization.Apply(row.GetProperty("precision").GetInt32() == 24 ? SacredX87Precision.Single : SacredX87Precision.Extended,
                    values, row.GetProperty("actor_flags").GetUInt32(), row.GetProperty("component_flags").GetUInt32(),
                    row.GetProperty("horse").GetBoolean(), rules.UsesSlowSpeedFloor(resource), rules.SharesCombatArtRegeneration(resource), row.GetProperty("movement_factor").GetSingle());
                if (!values.SequenceEqual(Convert.FromHexString(row.GetProperty("after_hex").GetString()!)))
                    throw new Exception($"Native finalization differs: {row}; actual {Convert.ToHexString(values)}");
                checks += values.Length;
            }
        return checks;
    }
}
