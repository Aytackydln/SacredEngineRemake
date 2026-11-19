using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin.Balance;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.GameBin.WeaponModifiers;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Pax.Hero.Objects;
using Sacred.GameplayHost.Equipment;
using Sacred.GameplayHost.Runtime;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class HostItemPostLoadChecks
{
    public static int Run(string root)
    {
        using var fixture = Read("native-host-item-postprocessing");
        using var pricing = Read("native-equipment-pricing");
        var balance = Bytes(pricing.RootElement, "balance_prefix_hex");
        var tuning = new SacredEquipmentGenerationTuning(EquipmentGenerationBalanceBinReader.Read(balance),
            EquipmentGenerationBalanceBinReader.ReadBonusCurves(balance), EquipmentNormalizationBalanceBinReader.Read(balance),
            EquipmentSocketBalanceBinReader.Read(balance), EquipmentPricingBalanceBinReader.Read(balance));
        var prices = SacredEquipmentPriceDefinitions.Read(Bytes(pricing.RootElement, "override_code_hex"));
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var reference = row.GetProperty("reference").GetUInt32();
            var descriptor = new ItemsPakEntry(new((ushort)row.GetProperty("type").GetUInt32(), 0, 128, 0),
                MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(Bytes(row, "descriptor_hex")), "");
            var source = SacredSavedItem.FromPayload(Bytes(row, "source_hex"), fixture.RootElement.GetProperty("format_version").GetByte(),
                descriptor.ModelDesc.Category == SacredItemCategory.Weapon);
            var modifier = MemoryMarshal.Read<SacredWeaponModifierLayout>(Bytes(row, "price_rule_hex"));
            var overrides = prices.Overrides(source.ItemResource);
            var context = new SacredEquipmentPriceContext(descriptor.ModelDesc.Category, modifier.ItemId0 != 0, modifier.Modifier,
                Bytes(row, "shared_hex")[35], overrides.ClearClassRestrictions, overrides.ClearLevelAndSkillRequirements);
            var saved = WorldEquipmentSavedImport.Prepare(source, descriptor, context, tuning);
            var references = new GoldObjectReferencePool(); references.Apply(GoldReferenceBatch.FromPayload(BitConverter.GetBytes(reference)));
            references.Consume([reference]);
            var factory = new WorldEquipmentInitialFactory(references, new(row.GetProperty("counter_before").GetByte()), new(), 1);
            var item = factory.RestoreAllocated(descriptor, Bytes(row, "initial_values_hex"), saved, row.GetProperty("scheduler_clock").GetDouble(), reference);
            var input = Bytes(row, "post_input_hex").AsSpan(0x118, 130).ToArray();
            input.CopyTo(item.Values, 0);
            var result = WorldEquipmentSavedPostLoad.Prepare(item, context, tuning);
            var native = Bytes(row, "item_hex");
            Check(result.AsSpan().SequenceEqual(native.AsSpan(0x118, 130)), $"native C2 normalized current values: type {descriptor.ItemIndex}, variant {row.GetProperty("variant").GetInt32()}");
            Check(item.Values.AsSpan().SequenceEqual(input), "private validation leaves current values unchanged");
            Check(item.Node.Active && references.Available == 0 && factory.Items.Count == 1 && !item.Generated && !item.PacketReady,
                "normalization is independent of allocation, publication and actor binding");
            Check(source.ToPayload().AsSpan().SequenceEqual(Bytes(row, "source_hex")), "source file stays unchanged");
        }
        return checks;
        void Check(bool ok, string why) { if (!ok) throw new Exception("Host item post-load: " + why); checks++; }
        JsonDocument Read(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence", name + ".json")));
    }
    private static byte[] Bytes(JsonElement row, string key) => Convert.FromHexString(row.GetProperty(key).GetString()!);
}
