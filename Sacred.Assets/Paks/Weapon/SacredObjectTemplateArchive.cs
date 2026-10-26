using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Assets.Paks.Items;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;

namespace Sacred.Assets.Paks.Weapon;

/// <summary>Original Items.pak descriptors and Weapon.pak instance defaults for object construction.
/// This retains authored values; it does not perform native price or level recalculation.</summary>
public sealed class SacredObjectTemplateArchive
{
    private readonly Dictionary<uint, ItemsPakEntry> _items;
    private readonly Dictionary<uint, byte[]> _instances;
    private SacredObjectTemplateArchive(Dictionary<uint, ItemsPakEntry> items, Dictionary<uint, byte[]> instances,
        Dictionary<uint, SacredEquipmentLayout> equipment)
        => (_items, _instances, Equipment) = (items, instances, equipment);
    public IReadOnlyDictionary<uint, SacredEquipmentLayout> Equipment { get; }
    public IReadOnlyDictionary<uint, ItemsPakEntry> Items => _items;

    public static SacredObjectTemplateArchive Load(string installationDirectory)
    {
        var items = ItemsPakArchive.Load(Path.Combine(installationDirectory, "PAK", "Items.pak"))
            .ToDictionary(item => (uint)item.ItemIndex);
        var data = File.ReadAllBytes(Path.Combine(installationDirectory, "PAK", "Weapon.pak"));
        return FromPayload(items, data);
    }

    public static SacredObjectTemplateArchive FromPayload(IReadOnlyDictionary<uint, ItemsPakEntry> items, ReadOnlySpan<byte> weaponData)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (weaponData.Length < WeaponPakHeaderLayout.SerializedSize) throw new InvalidDataException("Truncated Weapon.pak header.");
        var header = MemoryMarshal.Read<WeaponPakHeaderLayout>(weaponData);
        header.ValidateSignature();
        if (header.Version < 8) throw new InvalidDataException("Native Gold object templates require Weapon.pak version 8 or later.");
        if (header.EntryCount > (weaponData.Length - WeaponPakHeaderLayout.SerializedSize) / SacredEquipmentLayout.Size)
            throw new InvalidDataException("Truncated Weapon.pak template table.");
        var visuals = items.ToDictionary(pair => pair.Key, pair => pair.Value);
        var instances = new Dictionary<uint, byte[]>();
        var equipment = new Dictionary<uint, SacredEquipmentLayout>();
        var inheritance = new List<(uint Type, uint Parent)>();
        for (var index = 0; index < header.EntryCount; index++)
        {
            var bytes = weaponData.Slice(WeaponPakHeaderLayout.SerializedSize + index * SacredEquipmentLayout.Size, SacredEquipmentLayout.Size);
            var type = BinaryPrimitives.ReadUInt32LittleEndian(bytes[128..]);
            var parent = BinaryPrimitives.ReadUInt32LittleEndian(bytes[36..]);
            instances[type] = bytes[128..].ToArray();
            equipment[type] = MemoryMarshal.Read<SacredEquipmentLayout>(bytes);
            inheritance.Add((type, parent));
            if (visuals.TryGetValue(type, out var item))
            {
                var descriptor = item.ModelDesc.WithWeaponEntry(unchecked((ushort)index));
                if (descriptor.ResourceId != 0 && descriptor.ResourceId != type) descriptor = descriptor.WithItemId(type);
                visuals[type] = item with { ModelDesc = descriptor };
            }
        }
        // Native links every Weapon.pak row before copying base descriptors in file order.
        foreach (var (type, parent) in inheritance)
            if (parent > 0 && visuals.TryGetValue(parent, out var basis) && visuals.TryGetValue(type, out var child))
                visuals[type] = child with { ModelDesc = basis.ModelDesc.WithWeaponEntry(child.ModelDesc.WeaponEntry)
                        .WithItemId(child.ModelDesc.ResourceId != 0 ? child.ModelDesc.ResourceId : type),
                    ModelName = basis.ModelName };
        return new(visuals, instances, equipment);
    }

    public ItemsPakEntry GetItem(uint type) => _items.TryGetValue(type, out var item) ? item :
        throw new InvalidDataException($"CreateObj type {type} has no original Items.pak descriptor.");
    public byte[] GetInstance(uint type) => _instances.TryGetValue(type, out var bytes) ? bytes.ToArray() :
        throw new InvalidDataException($"CreateObj type {type} requires native Weapon.pak default-row resolution.");
}
