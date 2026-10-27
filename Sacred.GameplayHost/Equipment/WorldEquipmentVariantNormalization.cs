using Sacred.Assets.Equipment;
using Sacred.Core.Pak.Weapon;

namespace Sacred.GameplayHost.Equipment;

/// <summary>Concrete native resource/value mutation before installation. The
/// factory supplies its model reset and effective value view; the latter may
/// be shared values rather than the object's own 130-byte record.</summary>
public sealed class WorldEquipmentVariantNormalization(SacredEquipmentGenerationData data)
{
    public bool Apply(WorldEquipmentObject item, uint actorResource, Span<byte> ownValues,
        Action<WorldEquipmentObject> resetModel, Func<uint?> currentClassFlags)
    {
        var resource = item.Base.ObjectType;
        var setType = data.Templates.Equipment.TryGetValue(resource, out var shared) ? shared.SetType : (byte?)null;
        return SacredEquipmentVariantNormalization.Apply(ownValues, resource, actorResource, setType,
            () => new WorldEquipmentVariants(data).Resolve(resource, actorResource), replacement =>
            {
                item.Base = item.Base with { ObjectType = replacement };
                resetModel(item);
            }, currentClassFlags);
    }
}
