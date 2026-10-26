using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Sacred.Assets.Equipment;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin.Balance;
using Sacred.Assets.GameBin.EquipmentVariants;
using Sacred.Assets.GameBin.WeaponModifiers;
using Sacred.Assets.Paks.Weapon;
using Sacred.Core.GameBin.WeaponModifiers;
using Sacred.Core.Pak.Items;

internal static class EquipmentGenerationFixture
{
    public static SacredEquipmentGenerationData Read(string root, JsonElement source)
    {
        JsonDocument Evidence(string filename) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence", filename)));
        using var classes = Evidence("native-equipment-class-inference.json");
        using var associations = Evidence("native-equipment-modifier-lookup.json");
        using var variants = Evidence("native-equipment-variants.json");
        using var pricing = Evidence("native-equipment-pricing.json");
        var items = Convert.FromHexString(source.GetProperty("items_hex").GetString()!);
        var entries = new Dictionary<uint, ItemsPakEntry>();
        for (var at = 0; at < items.Length; at += ItemsPakEntryModelDescLayout.SerializedSize)
        {
            var id = (uint)(at / ItemsPakEntryModelDescLayout.SerializedSize);
            var model = items.AsSpan(at + 55, 32);
            var end = model.IndexOf((byte)0);
            entries.Add(id, new(new((ushort)id, (uint)at, 128, 0), MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(items.AsSpan(at)),
                Encoding.Latin1.GetString(end < 0 ? model : model[..end])));
        }
        var templates = SacredObjectTemplateArchive.FromPayload(entries, Convert.FromHexString(source.GetProperty("weapon_hex").GetString()!));
        var patterns = classes.RootElement.GetProperty("patterns").EnumerateArray().ToDictionary(row => row[0].GetUInt32(), row => row[1].GetString()!);
        var classRules = SacredEquipmentClassDefinitions.ReadCode(Convert.FromHexString(classes.RootElement.GetProperty("code_hex").GetString()!),
            classes.RootElement.GetProperty("address").GetUInt32(), type => patterns[type]);
        var selection = SacredEquipmentSelectionDefinitions.Read(Convert.FromHexString(associations.RootElement.GetProperty("selection_code_hex").GetString()!));
        var modifiers = WeaponModifiersBinArchive.Parse(Convert.FromHexString(source.GetProperty("wpmod_hex").GetString()!));
        var lookup = new SacredEquipmentModifierResolver(modifiers,
            templates.Equipment.ToDictionary(pair => pair.Key, pair => pair.Value.BaseItemId), selection.ModifierRedirects);
        var gate = SacredEquipmentSelectionClassGateDefinitions.ReadCode(Convert.FromHexString(variants.RootElement.GetProperty("gate_code_hex").GetString()!),
            variants.RootElement.GetProperty("replacement_source_minimum").GetUInt32(), variants.RootElement.GetProperty("marker").GetString()!);
        var names = SacredItemResourceNames.Read(Convert.FromHexString(associations.RootElement.GetProperty("resource_names_hex").GetString()!));
        var prices = SacredEquipmentPriceDefinitions.Read(Convert.FromHexString(pricing.RootElement.GetProperty("override_code_hex").GetString()!));
        var combatClasses = source.GetProperty("combat_art_classes").EnumerateObject().ToDictionary(row => uint.Parse(row.Name), row => row.Value.GetByte());
        return new(templates, lookup, EquipmentGenerationTuningBinReader.Read(Convert.FromHexString(source.GetProperty("balance_hex").GetString()!)),
            EquipmentVariantsBinArchive.Read(Convert.FromHexString(variants.RootElement.GetProperty("rust_hex").GetString()!)),
            classRules, gate, selection.PoolRules, type => combatClasses.GetValueOrDefault(type), names.Find, prices.Overrides);
    }
}
