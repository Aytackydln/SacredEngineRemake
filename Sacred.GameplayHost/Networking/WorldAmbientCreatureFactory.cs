using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Ambient Create(type,position,0,true,true): publishes constructor
/// values before EnterSector adds local flags and random direction.</summary>
public sealed class WorldAmbientCreatureFactory(WorldCreatureConstructionAssets assets, GoldObjectReferencePool references,
    GoldWorldReplica replicas, SacredSectorGrid sectors, SacredRandomSequence random, WorldParticipantPublications publications)
{
    private byte _allocationCounter;
    public void Validate(uint type)
    {
        if (!assets.Items.TryGetValue(type, out var item) || item.ModelDesc.Category != SacredItemCategory.Creature)
            throw new InvalidDataException($"Ambient resource {type} requires another native object factory.");
        var template = assets.Creatures.ResolveTemplate(item);
        if (template?.Class == SacredCreatureClass.Hero) throw new InvalidDataException("Ambient creation cannot construct a hero.");
        _ = assets.Constructors.EquipmentSelector(type);
    }
    public void Create(uint type, int tileX, int tileY)
    {
        Validate(type);
        if (!references.TryAllocate(out var reference)) throw new InvalidDataException("Ambient creature requires a native 0x153 reservation.");
        var item = assets.Items[type];
        var template = assets.Creatures.ResolveTemplate(item);
        var flags = assets.Constructors.Flags(type);
        var behavior = flags.BehaviorFlags;
        if (((template?.Flags ?? 0) & 0x20) != 0) behavior |= 0x01000000;
        if (((template?.Flags ?? 0) & 0x40) != 0) behavior |= 0x08000000;
        var tail = unchecked((ushort)(random.Next() % 6));
        _ = random.Next(); // Constructor's +0x208 jitter, not serialized in this packet.
        // Gold 0x426610 tests the creature category, independently of selection flags.
        var objectFlags = flags.ObjectFlags | 0x10400000;
        if (((ushort)item.ModelDesc.GraphicFlags & 0x100) != 0) objectFlags |= 0x800;
        var x = SacredWorldCoordinates.TileCenterToWorld(tileX);
        var y = SacredWorldCoordinates.TileCenterToWorld(tileY);
        var sector = checked((ushort)sectors.GetSector(tileX, tileY));
        if (sector == 0) throw new InvalidDataException("Ambient creature has no original sector.");
        var basis = new GoldObjectBaseState(reference, type, objectFlags, sector, x, y, 0, 0, 0, unchecked(++_allocationCounter), replicas.LocalPlayerSlot, 0);
        var physical = GoldPhysicalConstructionWriter.Write(basis, GoldPhysicalFacing.ToDirection(BitConverter.SingleToUInt32Bits(item.ModelDesc.Angle3D)));
        var progression = template is null ? new WorldCreatureProgression(type) : new WorldCreatureProgression(template);
        var creature = GoldCreatureConstructionWriter.Write(physical, progression.Write(0x16F),
            (uint)(template?.Class ?? SacredCreatureClass.Unknown), behavior, assets.Constructors.EquipmentSelector(type), tail);
        publications.Send(GoldObjectStateCodec.Create(0x16F, replicas.LocalPlayerSlot, creature.ToPayload()), applyLocally: true);
        var dy = (float)((random.Next() % 101 - 50) * (double)0.02f);
        var dx = (float)((random.Next() % 101 - 50) * (double)0.02f);
        replicas.InitializeOwnedCreatureDirection(reference, 0x90000000, new(dx, dy, 0));
        Console.WriteLine($"Network ambient creature created: ref {reference}, type {type}, sector {sector}, tile {tileX},{tileY}, level {progression.Level}.");
    }
}
