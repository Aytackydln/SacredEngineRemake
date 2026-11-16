using System.Collections.ObjectModel;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Runtime;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Equipment;

/// <summary>Gold 0x5FB530 fresh unpublished weapon/shield/compared Armor/QuestItem/Book paths. The ordinary
/// item constructor does not generate values or consume RNG. Reservations,
/// allocation counter and scheduler are shared with the caller's other objects.
/// Item generation/installation and publication remain separate obligations.</summary>
public sealed class WorldEquipmentInitialFactory
{
    private readonly Dictionary<uint, WorldEquipmentInitialItem> _items = [];
    private readonly List<uint> _graphicObjects = [];
    private readonly GoldObjectReferencePool _references;
    private readonly WorldObjectAllocationCounter _counter;
    private readonly byte _owner;

    public WorldEquipmentInitialFactory(GoldObjectReferencePool references, WorldObjectAllocationCounter counter,
        WorldObjectActivationQueue activation, byte owner)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(counter);
        ArgumentNullException.ThrowIfNull(activation);
        if (owner is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(owner));
        (_references, _counter, Activation, _owner) = (references, counter, activation, owner);
        Items = new ReadOnlyDictionary<uint, WorldEquipmentInitialItem>(_items);
        GraphicObjects = _graphicObjects.AsReadOnly();
    }

    public WorldObjectActivationQueue Activation { get; }
    public IReadOnlyDictionary<uint, WorldEquipmentInitialItem> Items { get; }
    public IReadOnlyList<uint> GraphicObjects { get; }

    public WorldEquipmentInitialItem Create(ItemsPakEntry descriptor, ReadOnlySpan<byte> originalValues,
        double schedulerClock, bool networking, bool owned = false)
    {
        if (descriptor.ModelDesc.Category is not (SacredItemCategory.Weapon or SacredItemCategory.Shield or SacredItemCategory.Helmet or SacredItemCategory.ChestArmor or
                SacredItemCategory.ShoulderArmor or SacredItemCategory.FootArmor or SacredItemCategory.ArmArmor or SacredItemCategory.Wings or SacredItemCategory.Belt or SacredItemCategory.QuestItem or SacredItemCategory.Book or SacredItemCategory.HorseEquipment))
            throw new NotSupportedException("Initial item factory requires a compared ordinary item constructor.");
        if (originalValues.Length != SacredEquipmentInstanceLayout.Size)
            throw new ArgumentException("Expected the original resolved 130-byte item record.", nameof(originalValues));
        if (!double.IsFinite(schedulerClock) || schedulerClock < 0)
            throw new ArgumentOutOfRangeException(nameof(schedulerClock));
        // Book 0x5CBFC0 does not forward factory event 0x40 to the physical handler.
        var direction = descriptor.ModelDesc.Category == SacredItemCategory.Book
            ? default : SacredStartupHeading.ToDirection(descriptor.ModelDesc.Angle3D);
        if (!_references.TryAllocate(out var reference))
            throw new InvalidDataException("Initial equipment construction needs a 0x153 object-reference reservation.");
        _counter.BeginAllocation();
        var physical = new WorldEquipmentObject(new(0, descriptor.ItemIndex, 2, 0, 0, 0, 0, 0, 0, _counter.Value, 0, 0), true, true);
        var item = new WorldEquipmentInitialItem(physical, originalValues.ToArray());
        _items.Add(reference, item);
        Activation.ActivateInitial(item.Node, schedulerClock);
        // Registry insertion and activation precede reference/owner assignment.
        var flags = physical.Base.Flags;
        if (owned) flags |= 0x10000000;
        if (descriptor.ModelDesc.RequiresGraphicObjectRegistration) flags |= 0x800;
        if (networking) flags |= 0x10000;
        physical.Base = physical.Base with { ObjectReference = reference, Owner = _owner, Flags = flags };
        physical.Direction = new(direction.X, direction.Y, direction.Z);
        if (descriptor.ModelDesc.RequiresGraphicObjectRegistration) _graphicObjects.Add(reference);
        return item;
    }
}
