using System.Buffers.Binary;
using System.Numerics;
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

internal static class HostModelReactivationChecks
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
        using var models = Read("native-host-model-reactivation");
        using var hero = Read("native-host-hero-post-load");
        var savedHero = SacredSavedCreature.FromPayload(Bytes(hero.RootElement, "source_hex"), hero.RootElement.GetProperty("format_version").GetByte());
        var checks = 0;
        foreach (var row in models.RootElement.GetProperty("cases").EnumerateArray())
        {
            var references = new GoldObjectReferencePool();
            var reserved = row.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("reference").GetUInt32()).ToArray();
            var payload = reserved.SelectMany(BitConverter.GetBytes).ToArray();
            references.Apply(GoldReferenceBatch.FromPayload(payload)); references.Consume(reserved);
            var queue = new WorldObjectActivationQueue(); var counter = new WorldObjectAllocationCounter(1);
            var factory = new WorldEquipmentInitialFactory(references, counter, queue, 1);
            var records = fixture.RootElement.GetProperty("cases").EnumerateArray().ToArray();
            var itemRows = row.GetProperty("items").EnumerateArray().ToArray();
            for (var index = 0; index < records.Length; index++)
            {
                var record = records[index]; var expectedItem = itemRows[index];
                var descriptor = new ItemsPakEntry(new((ushort)record.GetProperty("type").GetUInt32(), 0, 128, 0),
                    MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(Bytes(record, "descriptor_hex")), "");
                var source = SacredSavedItem.FromPayload(Bytes(record, "source_hex"), fixture.RootElement.GetProperty("format_version").GetByte(),
                    descriptor.ModelDesc.Category == SacredItemCategory.Weapon);
                var modifier = MemoryMarshal.Read<SacredWeaponModifierLayout>(Bytes(record, "price_rule_hex"));
                var overrides = prices.Overrides(source.ItemResource);
                var context = new SacredEquipmentPriceContext(descriptor.ModelDesc.Category, modifier.ItemId0 != 0, modifier.Modifier,
                    Bytes(record, "shared_hex")[35], overrides.ClearClassRestrictions, overrides.ClearLevelAndSkillRequirements);
                var state = WorldEquipmentSavedImport.Prepare(source, descriptor, context, tuning);
                var item = factory.RestoreAllocated(descriptor, Bytes(record, "initial_values_hex"), state, 1, expectedItem.GetProperty("reference").GetUInt32());
                WorldEquipmentSavedPostLoad.Complete(item, context, tuning);
                var beforeItem = Bytes(expectedItem, "before_hex");
                item.Physical.Base = item.Physical.Base with { Flags = Word(beforeItem, 20) };
                item.Physical.Vector7C = Vector(beforeItem, 0x7c); item.Physical.Vector88 = Vector(beforeItem, 0x88);
            }
            var before = Bytes(row, "before_hex"); var expected = Bytes(row, "actor_hex");
            var actor = new WorldCreatureInitialActor(new(1, Word(before, 16), Word(before, 20), Short(before, 24),
                unchecked((int)Word(before, 28)), unchecked((int)Word(before, 32)), before[36], Word(before, 40), Word(before, 52), before[56], before[57], Word(before, 60)),
                new(before.AsSpan(0x3b0, 238).ToArray(), before.AsSpan(0x4d0, 24).ToArray(), before[0x4ce], before[0x4cf]),
                Word(before, 0x1f0), Word(before, 0x1f4), 0, 0, 0, 0, [])
            {
                HeroPostLoadCompleted = true, SavedEquipmentReferencesBound = true, RestoredCreature = new(savedHero, []),
                RestoredPhysical = new(Word(before, 0x64), 0, 0, 0, 0, 0, default, default, default, default, default, 0, 0, Word(before, 0xb0), default, default, default, null, 10000, 1.5625),
                EquipmentReferences = Enumerable.Range(0,19).Select(index => Word(before, 0x1a4 + index * 4)).ToArray()
            };
            queue.ActivateInitial(actor.Node, 1);
            var result = WorldCreatureHeroModelReactivation.Prepare(actor, factory.Items, Vector3.Zero, WorldStartupEffectProjection.ComparedGold);
            WorldCreatureHeroModelReactivation.CompleteUnloadedSlots(result, factory.Items);
            Check(result.ModelReactivated && result.RestoredPhysical!.Animation == Word(expected, 0x64), "actual zero selector after body reopen");
            Check(result.Base == actor.Base && ReferenceEquals(result.Node, actor.Node), "same actor identity/node");
            var orientation = result.RestoredPhysical!.ModelOrientation!.Value;
            var values = new[] { orientation.M11,orientation.M12,orientation.M13,orientation.M21,orientation.M22,orientation.M23,orientation.M31,orientation.M32,orientation.M33 };
            var original = Convert.FromHexString(row.GetProperty("orientations")[0].GetString()!);
            for (var index=0;index<values.Length;index++) Check(Math.Abs(values[index]-BinaryPrimitives.ReadSingleLittleEndian(original.AsSpan(index*4)))<.000001f, "native new-body orientation");
            foreach (var itemRow in itemRows)
            {
                var item = factory.Items[itemRow.GetProperty("reference").GetUInt32()]; var native = Bytes(itemRow,"item_hex");
                Check(item.Physical.Base.Flags == Word(native,20) && item.Physical.Vector7C == Vector(native,0x7c) && item.Physical.Vector88 == Vector(native,0x88), "unloaded slot flags and placement cache");
                Check((item.Physical.Base.Flags&0x04000000)==0 && item.Physical.AttachmentParent==((Word(native,20)&0x400)!=0 ? Word(native,0x2c) : 0) && !item.PacketReady, "does not invent loaded attachment/publication");
            }
            try { WorldCreatureHeroModelReactivation.Prepare(actor with { KiMode=1 },factory.Items,Vector3.Zero,WorldStartupEffectProjection.ComparedGold); throw new Exception("Unverified action state accepted"); }
            catch(NotSupportedException) { checks++; }
            var firstItem=factory.Items[actor.EquipmentReferences.First(reference=>reference!=0)];
            var itemBase=firstItem.Physical.Base;
            firstItem.Physical.Base=itemBase with { Flags=itemBase.Flags|0x04000000 };
            try { WorldCreatureHeroModelReactivation.Prepare(actor,factory.Items,Vector3.Zero,WorldStartupEffectProjection.ComparedGold); throw new Exception("Loaded attachment bypassed"); }
            catch(NotSupportedException) { checks++; }
            firstItem.Physical.Base=itemBase;
            Check(counter.Value==15 && references.Available==0 && queue.ActivationCount==15, "no replacement allocation or scheduler node");
            Check(ReferenceEquals(result,WorldCreatureHeroModelReactivation.Prepare(result,factory.Items,Vector3.Zero,WorldStartupEffectProjection.ComparedGold)), "idempotent logical reactivation");
        }
        return checks;
        void Check(bool ok,string why) { if(!ok) throw new Exception("Host model reactivation: "+why);checks++; }
        JsonDocument Read(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence",name+".json")));
    }
    private static byte[] Bytes(JsonElement row,string key)=>Convert.FromHexString(row.GetProperty(key).GetString()!);
    private static uint Word(byte[] bytes,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
    private static ushort Short(byte[] bytes,int at)=>BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at));
    private static GoldPhysicalVector Vector(byte[] bytes,int at)=>new(BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at)),BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at+4)),BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at+8)));
}
