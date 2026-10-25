using System.Numerics;
using Sacred.Core.Pax.Hero.Inventory;

namespace Sacred.Network.Gameplay;

/// <summary>Receipt-time actor/target and ordered native event inputs. Consumers must check instance lifetimes.</summary>
public sealed record GoldCreatureFxCreation(uint Type, uint EventId, GoldObjectPosition Position,
    Vector3? ProjectionInput, uint Parameter);
public sealed record GoldCreatureInteractionWork(GoldCreatureInteractionEvent Event, GoldReplicatedObject Subject,
    GoldReplicatedObject? Target, uint? NativeEventId, IReadOnlyList<uint> Arguments)
{
    public GoldCreatureFxCreation? FxCreation { get; init; }
    public SacredSavedInventory? Inventory { get; init; }
}

public sealed class GoldCreatureInteractionsReplica
{
    private readonly List<GoldCreatureInteractionWork> _pending = [];
    public GoldCreatureInteractionsReplica() => Pending = _pending.AsReadOnly();
    public IReadOnlyList<GoldCreatureInteractionWork> Pending { get; }
    internal event Action<GoldCreatureInteractionWork>? Published;
    public GoldInventoryQuestRecordsReplica InventoryRecords { get; } = new();
    public GoldRegionObjectTypesReplica RegionTypes { get; } = new();
    public long ReceivedCount { get; private set; }
    public long RejectedCount { get; private set; }
    internal bool Apply(GoldCreatureInteractionEvent incoming, GoldWorldReplica world, Dictionary<uint, GoldReplicatedObject> objects)
    {
        var entry = GoldCreatureInteractionCodec.Decode(incoming.Message); ReceivedCount++;
        var local = world.LocalPlayerSlot != 0 ? world.LocalPlayerSlot : world.Session.Teams.LocalPlayerSlot;
        GoldReplicatedObject? Find(uint reference) => objects.GetValueOrDefault(reference);
        bool ActiveCreature(GoldReplicatedObject? obj) => obj?.Creature is not null && (obj.BaseState.Flags & 0x10) == 0;
        GoldReplicatedObject? subject; GoldReplicatedObject? target = null; uint? eventId = null;
        GoldCreatureFxCreation? creation = null;
        uint[] args = []; SacredSavedInventory? inventory = null;
        switch (entry)
        {
            case GoldAlternatePickupEvent pickup:
                // Gold selects the network sender, independently of the local slot. Missing item still dispatches 20C.
                subject = Find(entry.Message.Sender); target = Find(pickup.ItemReference);
                if (!ActiveCreature(subject)) return Reject();
                eventId = 0x20C; args = [pickup.ItemReference, pickup.FacingBits]; break;
            case GoldContainerOpenCommandEvent open:
                subject = Find(local); target = Find(open.ObjectReference);
                if (target?.Container is null || (target.BaseState.Flags & 0x10) != 0 || !ActiveCreature(subject)) return Reject();
                eventId = 0x20E; args = [open.ObjectReference]; break;
            case GoldInventoryInsertEvent insert:
                subject = Find(local); inventory = world.FindInventory(local);
                // The two native accessors require presence, with no additional inactive-creature test here.
                if (subject?.Creature is null || inventory is null) return Reject();
                args = [insert.KeyWord, insert.Word04, 0];
                InventoryRecords.Apply(local, inventory, insert.KeyWord, insert.Word04); break;
            case GoldCreatureDeathCommandEvent death:
                subject = Find(death.ObjectReference); if (!ActiveCreature(subject)) return Reject();
                objects[death.ObjectReference] = GoldCreatureDeathReducer.Apply(subject!, death);
                eventId = 0x208; args = [death.PlayerWord]; break;
            case GoldCreatureFxSpawnEvent fx:
                subject = Find(fx.ObjectReference);
                if (subject?.Creature is null || fx.Message.Opcode == 0x14A && !ActiveCreature(subject)) return Reject();
                eventId = fx.NativeEventId; args = fx.Operands.ToArray();
                creation = fx.Message.Opcode == 0x14A
                    ? new(fx.FxType, fx.NativeEventId, new(unchecked((int)args[0]), unchecked((int)args[1]), (byte)args[2], (ushort)args[3]), null, 0)
                    : new(fx.FxType, fx.NativeEventId, new(subject.BaseState.X, subject.BaseState.Y, subject.BaseState.Surface, subject.BaseState.Sector),
                        new Vector3((float)args[0], (float)args[1], (float)args[2]), fx.Message.Opcode == 0x14D ? args[3] : 0);
                if (fx.RetiresSource && fx.ObjectReference != local)
                {
                    // The ordinary participant retirement helper protects the local player; host-only bypass is separate.
                    // Foreign source retirement proceeds regardless of FX lookup success.
                    GoldObjectLifecycleReducer.Apply(objects, new GoldObjectRemovalEvent(entry.Message, fx.ObjectReference));
                }
                break;
            case GoldRegionObjectTypesEvent types:
                return RegionTypes.Apply(types, objects);
            default: throw new InvalidDataException("Unknown Gold interaction event.");
        }
        _pending.Add(new(entry, subject!, target, eventId, Array.AsReadOnly(args)) { Inventory = inventory, FxCreation = creation });
        Published?.Invoke(_pending[^1]);
        return true;
        bool Reject() { RejectedCount++; return false; }
    }
    internal void Clear() { _pending.Clear(); RegionTypes.Clear(); InventoryRecords.Clear(); ReceivedCount = RejectedCount = 0; }
}
