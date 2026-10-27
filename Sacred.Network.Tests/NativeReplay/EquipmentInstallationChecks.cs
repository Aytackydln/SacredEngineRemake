using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Pax.Hero.Objects;
using Sacred.GameplayHost.Equipment;
using Sacred.Network.Gameplay;

internal static class EquipmentInstallationChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-installation.json")));
        var json = fixture.RootElement;
        var defaults = SacredEquipmentDefaultDefinitions.Read(Image(json));
        var variants = json.GetProperty("definition_variants").EnumerateArray()
            .Select(value => SacredEquipmentDefaultDefinitions.Read(Image(value))).Append(defaults).ToArray();
        var checks = 0;
        foreach (var row in json.GetProperty("defaults").EnumerateArray())
            foreach (var variant in variants)
            {
                Equal(variant.Resource(row[0].GetUInt32(), row[1].GetUInt16()), row[2].GetUInt32(), "native default resource");
                checks++;
            }
        foreach (var row in json.GetProperty("cases").EnumerateArray())
        {
            var input = row.GetProperty("inputs");
            var slot = input.GetProperty("slot").GetUInt16();
            var slots = new uint[19];
            if (slot < 19) slots[slot] = U(input, "old_ref");
            var actor = new WorldCreatureEquipmentState(new(100, U(input, "actor_resource"), 0x12340080,
                0, 0, 0, 0, 0, 0, 0, 0, 0), B(input, "horse"), slots) { Behavior = U(input, "behavior") };
            var incoming = Item(500, 1729, "new", input);
            var old = Item(600, U(input, "old_resource"), "old", input);
            var effects = new Effects(input, defaults, incoming, old);
            var installer = new WorldCreatureEquipmentInstallation(effects, B(input, "network"));
            var result = input.GetProperty("operation").GetString() == "install"
                ? installer.Install(actor, slot, U(input, "incoming_ref"), B(input, "publish"))
                : installer.Detach(actor, slot, B(input, "publish"));
            Equal(result, U(row, "result"), "return");
            Equal(actor.Base.Flags, U(row, "actor_flags"), "actor flags");
            if (!actor.References.SequenceEqual(row.GetProperty("slots").EnumerateArray().Select(x => x.GetUInt32()).ToArray()))
                throw new Exception($"Native installation slots differ: {row}");
            CheckObject(incoming, row.GetProperty("incoming"));
            CheckObject(old, row.GetProperty("old"));
            var retained = actor.Retained.Select(value => new uint[] { value.Reference, value.Value, value.Flags });
            EqualJson(retained, row.GetProperty("retained"), "retained equipment", row);
            EqualJson(effects.Trace, row.GetProperty("trace"), "ordered effects", row);
            checks += 19 + 2 * 9 + 4;
        }
        checks += CheckSavedLinks();
        return checks;
    }

    private static WorldEquipmentObject Item(uint reference, uint resource, string prefix, JsonElement input)
    {
        var result = new WorldEquipmentObject(new(reference, resource, U(input, prefix + "_flags"),
            55, 12345, 67890, 3, 0, 456, 0, 9, 0), B(input, prefix == "new" ? "new_physical" : "old_physical"),
            B(input, prefix == "new" ? "new_item" : "old_item")) { PhysicalFlags = U(input, prefix + "_physical_flags") };
        result.SetLink(123, (result.Base.Flags & 0x400) != 0);
        return result;
    }

    private static void CheckObject(WorldEquipmentObject actual, JsonElement expected)
    {
        Equal(actual.Base.Flags, U(expected, "flags"), "object flags");
        Equal(actual.Link, U(expected, "link"), "attachment/world link");
        Equal(actual.Base.Word34, U(expected, "trigger"), "trigger");
        Equal(actual.PhysicalFlags, U(expected, "physical_flags"), "physical flags");
        Equal(actual.Base.Sector, U(expected, "sector"), "sector");
        Equal(unchecked((uint)actual.Base.X), U(expected, "x"), "x");
        Equal(unchecked((uint)actual.Base.Y), U(expected, "y"), "y");
        Equal(actual.Base.Surface, U(expected, "surface"), "surface");
        Equal(actual.Base.Owner, U(expected, "owner"), "peer owner preserved");
    }

    private static int CheckSavedLinks()
    {
        var bytes = new byte[SacredSavedObjectBaseLayout.Size];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(37), 123);
        var plain = System.Runtime.InteropServices.MemoryMarshal.Read<SacredSavedObjectBaseLayout>(bytes);
        Equal(plain.WorldLink, 123, "saved world link"); Equal(plain.AttachmentParent, 0, "saved unparented link");
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(18), 0x400);
        var attached = System.Runtime.InteropServices.MemoryMarshal.Read<SacredSavedObjectBaseLayout>(bytes);
        Equal(attached.WorldLink, 0, "saved attached world link"); Equal(attached.AttachmentParent, 123, "saved parent");
        return 4;
    }

    private sealed class Effects(JsonElement input, SacredEquipmentDefaultDefinitions defaults,
        WorldEquipmentObject incoming, WorldEquipmentObject old) : IWorldCreatureEquipmentEffects
    {
        private readonly Dictionary<uint, WorldEquipmentObject> _objects = new() { [500] = incoming, [600] = old };
        public List<object[]> Trace { get; } = [];
        private void Log(params object[] values) => Trace.Add(values);
        public WorldEquipmentObject? Find(uint reference) => reference == 600 && !B(input, "old_exists") ? null : _objects.GetValueOrDefault(reference);
        public bool Exists(uint reference) => reference != 600 || B(input, "old_exists") ? _objects.ContainsKey(reference) : false;
        public uint DefaultResource(uint actorResource, ushort slot) => defaults.Resource(actorResource, slot);
        public uint CreateDefault(uint resource)
        {
            Log("create", resource);
            _objects[700] = new(new(700, resource, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), true, true);
            return 700;
        }
        public void Delete(uint reference) { Log("delete", reference); _objects.Remove(reference); }
        public void Normalize(WorldEquipmentObject item, uint actorResource) => Log("normalize", item.Base.ObjectReference, actorResource);
        public void EnsureActorModel(WorldCreatureEquipmentState actor) => Log("ensure");
        public void ResetItemPlacement(WorldEquipmentObject item, WorldCreatureEquipmentState actor) => Log("placement", item.Base.ObjectReference);
        public void DetachModel(WorldEquipmentObject item, ushort slot)
        {
            var mode = input.GetProperty("mode").GetInt32();
            if (mode is 1 or 2) Log("detach-model", item.Base.ObjectReference, mode == 1 || B(input, "merged_model") ? 1 : 0);
        }
        public void PrepareAttachment(WorldEquipmentObject item)
        {
            Log("motion", item.Base.ObjectReference);
            Log("direction", item.Base.ObjectReference);
        }
        public ushort AttachModel(WorldCreatureEquipmentState actor, ushort slot)
        {
            Log("attach", slot);
            return input.GetProperty("attachment_slot").GetUInt16();
        }
        public void Recalculate(WorldCreatureEquipmentState actor) => Log("calculate");
        public void EquipmentChanged(WorldCreatureEquipmentState actor, ushort slot) => Log("changed", slot);
        public void RetireFromWorld(WorldEquipmentObject item) => Log("retire", item.Base.ObjectReference);
        public void SetMount(uint reference) => Log("mount", reference);
        public bool PublishMountRemoval(WorldCreatureEquipmentState actor, WorldEquipmentObject? mount)
            => !B(input, "special_mount") || mount is not null && U(input, "mount_state") < 2;
        public void Publish(ushort opcode, uint actor, ushort slot, uint item) => Log("publish", opcode, actor, slot, item);
    }

    private static void Equal(uint actual, uint expected, string field)
    {
        if (actual != expected) throw new Exception($"Native installation {field}: expected {expected:X8}, actual {actual:X8}.");
    }
    private static void EqualJson<T>(T actual, JsonElement expected, string field, JsonElement row)
    {
        if (JsonSerializer.Serialize(actual) != expected.GetRawText())
            throw new Exception($"Native installation {field} differs. Actual {JsonSerializer.Serialize(actual)}; case {row}");
    }
    private static uint U(JsonElement row, string name) => row.GetProperty(name).GetUInt32();
    private static bool B(JsonElement row, string name) => row.GetProperty(name).GetBoolean();
    private static byte[] Image(JsonElement json)
    {
        var image = new byte[json.GetProperty("image_size").GetInt32()];
        foreach (var chunk in json.GetProperty("definition_chunks").EnumerateArray())
            Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
        return image;
    }
}
