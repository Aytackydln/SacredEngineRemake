namespace Sacred.GameplayHost.Equipment;

/// <summary>Required native services, deliberately without successful no-op
/// defaults. Model/placement, concrete value recalculation and publication must
/// be resolved by the factory before this can complete startup construction.</summary>
public interface IWorldCreatureEquipmentEffects
{
    WorldEquipmentObject? Find(uint reference);
    bool Exists(uint reference);
    uint DefaultResource(uint actorResource, ushort slot);
    uint CreateDefault(uint resource);
    void Delete(uint reference);
    void Normalize(WorldEquipmentObject item, uint actorResource);
    void EnsureActorModel(WorldCreatureEquipmentState actor);
    void ResetItemPlacement(WorldEquipmentObject item, WorldCreatureEquipmentState actor);
    void DetachModel(WorldEquipmentObject item, ushort slot);
    void PrepareAttachment(WorldEquipmentObject item);
    ushort AttachModel(WorldCreatureEquipmentState actor, ushort slot);
    void Recalculate(WorldCreatureEquipmentState actor);
    void EquipmentChanged(WorldCreatureEquipmentState actor, ushort slot);
    void RetireFromWorld(WorldEquipmentObject item);
    void SetMount(uint reference);
    bool PublishMountRemoval(WorldCreatureEquipmentState actor, WorldEquipmentObject? mount);
    void Publish(ushort opcode, uint actor, ushort slot, uint item);
}
