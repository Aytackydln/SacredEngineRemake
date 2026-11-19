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

internal static class HostSavedLoaderTailChecks
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
        using var models = Read("native-host-saved-loader-tail");
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
            var descriptors = new Dictionary<uint, ItemsPakEntry>();
            var itemRows = row.GetProperty("items").EnumerateArray().ToArray();
            for (var index = 0; index < records.Length; index++)
            {
                var record = records[index]; var expectedItem = itemRows[index];
                var descriptor = new ItemsPakEntry(new((ushort)record.GetProperty("type").GetUInt32(), 0, 128, 0),
                    MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(Bytes(record, "descriptor_hex")), "");
                descriptors[descriptor.ItemIndex] = descriptor;
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
                item.Physical.Base = item.Physical.Base with { Flags = Word(beforeItem, 20), Sector = Short(beforeItem,24), X = unchecked((int)Word(beforeItem,28)), Y = unchecked((int)Word(beforeItem,32)), Surface = beforeItem[36] };
                item.Physical.Vector7C = Vector(beforeItem, 0x7c); item.Physical.Vector88 = Vector(beforeItem, 0x88);
            }
            var before = Bytes(row, "before_hex"); var expected = Bytes(row, "actor_hex");
            var actor = new WorldCreatureInitialActor(new(1, Word(before, 16), Word(before, 20), Short(before, 24),
                unchecked((int)Word(before, 28)), unchecked((int)Word(before, 32)), before[36], Word(before, 40), Word(before, 52), before[56], before[57], Word(before, 60)),
                new(before.AsSpan(0x3b0, 238).ToArray(), before.AsSpan(0x4d0, 24).ToArray(), before[0x4ce], before[0x4cf]),
                Word(before, 0x1f0), Word(before, 0x1f4), 0, 0, 0, 0, [])
            {
                ModelReactivated = true, Direction = Vector(before,0x70), HeroPostLoadCompleted = true, SavedEquipmentReferencesBound = true, RestoredCreature = new(savedHero, []),
                RestoredPhysical = new(Word(before, 0x64), 0, 0, 0, 0, 0, default, default, default, default, default, 0, 0, Word(before, 0xb0), default, default, default, null, 10000, 1.5625),
                EquipmentReferences = Enumerable.Range(0,19).Select(index => Word(before, 0x1a4 + index * 4)).ToArray()
            };
            queue.ActivateInitial(actor.Node, 1);
            var descriptorBytes = new byte[128];
            BinaryPrimitives.WriteSingleLittleEndian(descriptorBytes.AsSpan(87), row.GetProperty("angle").GetSingle());
            var heroDescriptor = new ItemsPakEntry(new((ushort)actor.Base.ObjectType, 0, 128, 0), MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(descriptorBytes), "");
            WorldCreatureInitialActor Prepare(WorldCreatureInitialActor value, Func<uint,ushort,uint>? defaults = null) =>
                WorldCreatureSavedLoaderTail.Prepare(value, factory.Items, heroDescriptor, type => descriptors[type], defaults ?? ((_,_) => 0), WorldStartupEffectProjection.ComparedGold);
            var result = Prepare(actor);
            WorldCreatureSavedLoaderTail.CommitItems(result, factory.Items);
            Check(result.SavedLoaderTailPrepared && result.Direction == Vector(expected,0x70), "native authored-heading event");
            var resources = factory.Items.ToDictionary(pair=>pair.Key,pair=>pair.Value.Physical.Base.ObjectType);
            foreach(var inventory in row.GetProperty("inventories").EnumerateArray())
            {
                var prior = InventoryPayload(Bytes(inventory,"before_hex"));
                var expectedInventory = InventoryPayload(Bytes(inventory,"inventory_hex"));
                var fixedInventory = GoldSavedInventoryImporter.Import(Sacred.Core.Pax.Hero.Inventory.SacredSavedInventory.FromPayload(prior),resources);
                Check(fixedInventory.Inventory.ToPayload().AsSpan(0,4436).SequenceEqual(expectedInventory.AsSpan(0,4436)), "both actual native inventory fixes");
                foreach(var reference in fixedInventory.ClearRemovalFlagReferences)
                {
                    var item=factory.Items[reference];
                    item.Physical.Base=item.Physical.Base with { Flags=item.Physical.Base.Flags & ~0x10u };
                }
            }
            Check(result.Base == actor.Base && ReferenceEquals(result.Node, actor.Node), "same actor identity/node");
            var orientation = result.RestoredPhysical!.ModelOrientation!.Value;
            var values = new[] { orientation.M11,orientation.M12,orientation.M13,orientation.M21,orientation.M22,orientation.M23,orientation.M31,orientation.M32,orientation.M33 };
            var original = Convert.FromHexString(row.GetProperty("orientations")[0].GetString()!);
            for (var index=0;index<values.Length;index++) Check(Math.Abs(values[index]-BinaryPrimitives.ReadSingleLittleEndian(original.AsSpan(index*4)))<.000001f, "native final-heading orientation");
            foreach (var itemRow in itemRows)
            {
                var item = factory.Items[itemRow.GetProperty("reference").GetUInt32()]; var native = Bytes(itemRow,"item_hex");
                Check(item.Physical.Base.Sector == Short(native,24) && item.Physical.Base.X == unchecked((int)Word(native,28)) && item.Physical.Base.Y == unchecked((int)Word(native,32)) && item.Physical.Base.Surface == native[36], "native bound-item coordinates");
                Check(item.Physical.Base.Flags == Word(native,20) && item.Physical.Vector7C == Vector(native,0x7c) && item.Physical.Vector88 == Vector(native,0x88), "unloaded slot flags and placement cache");
                Check((item.Physical.Base.Flags&0x04000000)==0 && item.Physical.AttachmentParent==((Word(native,20)&0x400)!=0 ? Word(native,0x2c) : 0) && !item.PacketReady, "does not invent loaded attachment/publication");
            }
            try { Prepare(actor with { KiMode=1 }); throw new Exception("Unverified action state accepted"); }
            catch(NotSupportedException) { checks++; }
            try { Prepare(actor, (_,_)=>1); throw new Exception("Default-item action bypassed"); }
            catch(NotSupportedException) { checks++; }
            var firstItem=factory.Items[actor.EquipmentReferences.First(reference=>reference!=0)];
            var itemBase=firstItem.Physical.Base;
            firstItem.Physical.Base=itemBase with { Flags=itemBase.Flags & ~2u };
            try { Prepare(actor); throw new Exception("World retirement bypassed"); }
            catch(NotSupportedException) { checks++; }
            firstItem.Physical.Base=itemBase;
            Check(counter.Value==15 && references.Available==0 && queue.ActivationCount==15, "no replacement allocation or scheduler node");
            Check(ReferenceEquals(result,Prepare(result)), "idempotent logical reactivation");
        }
        return checks;
        void Check(bool ok,string why) { if(!ok) throw new Exception("Host saved-loader tail: "+why);checks++; }
        JsonDocument Read(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence",name+".json")));
    }
    private static byte[] InventoryPayload(byte[] state)
    {
        // Native variable-list contents are unchanged by 5DE8E0; this comparison
        // covers all fixed inventory fields with empty independent trailing lists.
        var payload=new byte[4444];
        Copy(0x1160,0,8); Copy(0x115c,8,4); Copy(0xd5c,12,1024); Copy(0x18,1036,3072);
        Copy(0xc18,4108,120); Copy(0xc90,4228,200); Copy(0xd58,4428,4); Copy(0x1168,4432,4);
        return payload;
        void Copy(int source,int target,int count)=>state.AsSpan(source,count).CopyTo(payload.AsSpan(target));
    }
    private static byte[] Bytes(JsonElement row,string key)=>Convert.FromHexString(row.GetProperty(key).GetString()!);
    private static uint Word(byte[] bytes,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
    private static ushort Short(byte[] bytes,int at)=>BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at));
    private static GoldPhysicalVector Vector(byte[] bytes,int at)=>new(BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at)),BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at+4)),BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at+8)));
}


