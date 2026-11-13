using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Pak.Items;

internal static class EquipmentBindingChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-bindings.json")));
        var checks = 0;
        foreach (var variant in fixture.RootElement.GetProperty("variants").EnumerateArray())
        {
            var image = new byte[variant.GetProperty("image_size").GetInt32()];
            foreach (var chunk in variant.GetProperty("definition_chunks").EnumerateArray())
                Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
            var definitions = SacredEquipmentBindingDefinitions.Read(image);
            var kinds = Convert.FromHexString(variant.GetProperty("kind_hex").GetString()!);
            for (var slot = 0; slot < kinds.Length; slot++)
            {
                if ((int)definitions.Kind((ushort)slot) != kinds[slot]) throw new Exception($"Native binding kind differs: {slot}");
                checks++;
            }
            foreach (var row in variant.GetProperty("cases").EnumerateArray())
            {
                var actual = definitions.TargetBone(row[0].GetUInt16(), (SacredItemCategory)row[1].GetByte());
                if (actual != row[2].GetString()) throw new Exception($"Native {variant.GetProperty("name").GetString()} initial binding target differs: {row}: {actual ?? "<null>"}");
                checks++;
            }
        }
        return checks;
    }
}
