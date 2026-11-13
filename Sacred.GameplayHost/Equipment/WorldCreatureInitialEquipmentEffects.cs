using System.Buffers.Binary;
using Sacred.Assets.Equipment;
using Sacred.Core.Pak.Weapon;
using Sacred.GameplayHost.Scripting;

namespace Sacred.GameplayHost.Equipment;

/// <summary>Concrete effects for initial target-empty NPC installation, with
/// unloaded models and no default replacement. Other lifetime/model/publication
/// bodies remain explicit failures; no required effect has a successful default.</summary>
internal sealed class WorldCreatureInitialEquipmentEffects(SacredEquipmentGenerationData data,
    IReadOnlyDictionary<uint, WorldEquipmentInitialItem> items, WorldCreatureInitialActor actor) : IWorldCreatureEquipmentEffects
{
    public WorldEquipmentObject? Find(uint reference) => items.TryGetValue(reference, out var item) ? item.Physical : null;
    public bool Exists(uint reference) => items.ContainsKey(reference);
    public uint DefaultResource(uint actorResource, ushort slot) =>
        (data.EquipmentDefaults ?? throw Missing("original default resources")).Resource(actorResource, slot);

    public void Normalize(WorldEquipmentObject item, uint actorResource)
    {
        var own = items[item.Base.ObjectReference].Values;
        _ = new WorldEquipmentVariantNormalization(data).Apply(item, actorResource, own, WorldUnloadedModelEffects.Reset, () =>
        {
            SacredEquipmentDamageBounds.Normalize(own);
            return BinaryPrimitives.ReadUInt32LittleEndian(own.AsSpan(4));
        });
    }

    public void PrepareAttachment(WorldEquipmentObject item) => WorldUnloadedModelEffects.PrepareInitialAttachment(item);
    public ushort AttachModel(WorldCreatureEquipmentState state, ushort slot) => WorldUnloadedModelEffects.Attach(state, slot, Find);
    public void EquipmentChanged(WorldCreatureEquipmentState state, ushort slot) => WorldCreatureConstructionEvents.EquipmentChanged(actor.KiMode, slot);

    public uint CreateDefault(uint resource) => throw Missing("default-item construction");
    public void Delete(uint reference) => throw Missing("item retirement");
    public void EnsureActorModel(WorldCreatureEquipmentState state) => throw Missing("loaded actor model preparation");
    public void ResetItemPlacement(WorldEquipmentObject item, WorldCreatureEquipmentState state) => throw Missing("old-item placement");
    public void DetachModel(WorldEquipmentObject item, ushort slot) => throw Missing("old-item model detachment");
    public void Recalculate(WorldCreatureEquipmentState state) => throw Missing("replacement recalculation");
    public void RetireFromWorld(WorldEquipmentObject item) => throw Missing("placed-item sector retirement");
    public void SetMount(uint reference) => throw Missing("mount installation");
    public bool PublishMountRemoval(WorldCreatureEquipmentState state, WorldEquipmentObject? mount) => throw Missing("mount removal publication");
    public void Publish(ushort opcode, uint reference, ushort slot, uint item) => throw Missing("live installation publication");
    private static NotSupportedException Missing(string body) => new($"Initial equipment installation requires its separate native {body} body.");
}
