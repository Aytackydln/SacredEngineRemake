using System.Buffers.Binary;
using Sacred.Assets.Equipment;
using Sacred.Assets.Executable;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Equipment;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Reached initial Container/QuestItem/Book 487AB0..487ECE: actual hero variant check
/// followed by a no-modifier 5D5080 return. The caller still consumes both RNG
/// draws. Generated values/resource replacement require their separate callers.</summary>
public static class WorldScriptObjectValues
{
    public static byte[] Prepare(uint resource, ReadOnlySpan<byte> original, uint heroResource,
        SacredEquipmentGenerationData data, SacredScriptObjectValueDefinitions definitions, SacredRandomSequence random)
    {
        if (original.Length != SacredEquipmentInstanceLayout.Size ||
            data.Templates.GetItem(resource).ModelDesc.Category is not (SacredItemCategory.Container or SacredItemCategory.QuestItem or SacredItemCategory.Book))
            throw new NotSupportedException("CreateObj values require the actual original compared item-base instance.");
        var values = original.ToArray();
        if (definitions.RetainedResources.Contains(resource)) return values;
        var basis = resource;
        for (var index = 0; index < data.Classes.MaximumParentLinks && data.Classes.ParentResources.Any(range => range.Contains(basis)); index++)
            basis = data.ParentResource(basis) ?? 0;
        if (data.Modifiers.Find(basis) is not null)
            throw new NotSupportedException("CreateObj item has a native value modifier; its complete generation caller is required.");
        var setType = data.Templates.TryGetSharedTemplate(resource, out var shared) ? shared.SetType : (byte?)null;
        _ = SacredEquipmentVariantNormalization.Apply(values, resource, heroResource, setType,
            () => new WorldEquipmentVariants(data).Resolve(resource, heroResource),
            _ => throw new NotSupportedException("CreateObj item requires its actual resource replacement/model reset before generation."),
            () => BinaryPrimitives.ReadUInt32LittleEndian(values.AsSpan(4)));
        // Original quality selection and legacy selector still draw even when
        // the subsequent no-modifier generator leaves every value byte alone.
        _ = random.Next();
        _ = random.Next();
        return values;
    }
}
