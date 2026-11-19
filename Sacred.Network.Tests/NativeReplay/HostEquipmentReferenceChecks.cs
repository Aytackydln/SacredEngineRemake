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

internal static class HostEquipmentReferenceChecks
{
    public static int Run(string root)
    {
        using var fixture = Read("native-host-saved-items");
        using var pricing = Read("native-equipment-pricing");
        var balance = Bytes(pricing.RootElement, "balance_prefix_hex");
        var tuning = new SacredEquipmentGenerationTuning(EquipmentGenerationBalanceBinReader.Read(balance),
            EquipmentGenerationBalanceBinReader.ReadBonusCurves(balance), EquipmentNormalizationBalanceBinReader.Read(balance),
            EquipmentSocketBalanceBinReader.Read(balance), EquipmentPricingBalanceBinReader.Read(balance));
        var prices = SacredEquipmentPriceDefinitions.Read(Bytes(pricing.RootElement, "override_code_hex"));
        using var bindingProof = Read("native-host-equipment-references");
        using var heroProof = Read("native-host-hero-post-load");
        var savedHero = SacredSavedCreature.FromPayload(Bytes(heroProof.RootElement, "source_hex"), heroProof.RootElement.GetProperty("format_version").GetByte());
        var checks = 0;
        var executable = new byte[bindingProof.RootElement.GetProperty("image_size").GetInt32()];
        foreach (var chunk in bindingProof.RootElement.GetProperty("definition_chunks").EnumerateArray())
            Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(executable, chunk.GetProperty("offset").GetInt32());
        var modes = SacredSavedInventoryModeDefinitions.Read(executable);
        foreach (var mode in bindingProof.RootElement.GetProperty("inventory_modes").EnumerateArray())
            Check(modes.RequiresTransition(mode[0].GetUInt32()) == (mode[1].GetInt32() != 0), "actual native C2 inventory-mode predicate");
        foreach (var row in bindingProof.RootElement.GetProperty("cases").EnumerateArray())
        {
            var mapping = row.GetProperty("mapping").EnumerateArray().ToDictionary(pair => pair[0].GetUInt32(), pair => pair[1].GetUInt32());
            var reserved = mapping.Values.ToArray();
            var payload = new byte[reserved.Length * 4];
            for (var index = 0; index < reserved.Length; index++) BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(index * 4), reserved[index]);
            var references = new GoldObjectReferencePool(); references.Apply(GoldReferenceBatch.FromPayload(payload)); references.Consume(reserved);
            var queue = new WorldObjectActivationQueue();
            var counter = new WorldObjectAllocationCounter(1);
            var factory = new WorldEquipmentInitialFactory(references, counter, queue, 1);
            foreach (var itemRow in fixture.RootElement.GetProperty("cases").EnumerateArray())
            {
                var descriptor = new ItemsPakEntry(new((ushort)itemRow.GetProperty("type").GetUInt32(), 0, 128, 0),
                    MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(Bytes(itemRow, "descriptor_hex")), "");
                var source = SacredSavedItem.FromPayload(Bytes(itemRow, "source_hex"), fixture.RootElement.GetProperty("format_version").GetByte(),
                    descriptor.ModelDesc.Category == SacredItemCategory.Weapon);
                var modifier = MemoryMarshal.Read<SacredWeaponModifierLayout>(Bytes(itemRow, "price_rule_hex"));
                var overrides = prices.Overrides(source.ItemResource);
                var context = new SacredEquipmentPriceContext(descriptor.ModelDesc.Category, modifier.ItemId0 != 0, modifier.Modifier,
                    Bytes(itemRow, "shared_hex")[35], overrides.ClearClassRestrictions, overrides.ClearLevelAndSkillRequirements);
                var saved = WorldEquipmentSavedImport.Prepare(source, descriptor, context, tuning);
                factory.RestoreAllocated(descriptor, Bytes(itemRow, "initial_values_hex"), saved, 1, mapping[source.BaseState.ObjectReference]);
            }
            var actor = new WorldCreatureInitialActor(new(1, savedHero.BaseState.Resource, savedHero.BaseState.Flags, 0, 0, 0, 0, 0, 0, 1, 1, 0),
                new(new byte[238], new byte[24], 6, 1), 1, 0, 0, 0, 0, 0, [])
                { RestoredCreature = new(savedHero, []), HeroPostLoadCompleted = true };
            queue.ActivateInitial(actor.Node, 1);
            var bound = WorldCreatureSavedEquipmentReferences.Bind(actor, mapping, factory.Items);
            var expected = Bytes(row, "equipment_hex");
            for (var slot = 0; slot < bound.EquipmentReferences.Length; slot++)
                Check(bound.EquipmentReferences[slot] == BinaryPrimitives.ReadUInt32LittleEndian(expected.AsSpan(slot * 4)), "native two-pass slot result");
            Check(bound.SavedEquipmentReferencesBound && ReferenceEquals(bound.Node, actor.Node) && actor.EquipmentReferences.All(value => value == 0), "same actor node; original pending actor unchanged");
            Check(references.Available == 0 && counter.Value == 15 && factory.Items.Count == 14, "binding consumes no grants or objects");
            Check(ReferenceEquals(bound, WorldCreatureSavedEquipmentReferences.Bind(bound, mapping, factory.Items)), "idempotent binding");
            var invalid = new Dictionary<uint, uint>(mapping);
            invalid[savedHero.GetEquipmentReference(0)] = 16000;
            try { WorldCreatureSavedEquipmentReferences.Bind(actor, invalid, factory.Items); throw new Exception("Missing actual item accepted"); }
            catch (InvalidDataException) { checks++; }
            Check(actor.EquipmentReferences.All(value => value == 0), "rejection is atomic");
        }
        return checks;
        void Check(bool ok, string why) { if (!ok) throw new Exception("Host equipment references: " + why); checks++; }
        JsonDocument Read(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence", name + ".json")));
    }
    private static byte[] Bytes(JsonElement row, string key) => Convert.FromHexString(row.GetProperty(key).GetString()!);
}
