using Sacred.Assets.Equipment;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Equipment;

namespace Sacred.GameplayHost.Scripting;

/// <summary>CreateNPC 484D29..484DFA: 427A40 accepts category Weapon only.
/// Shield construction retains its original own values, name and RNG state.</summary>
public static class WorldScriptCreatureEquipmentValues
{
    public static void Apply(WorldEquipmentInitialItem item, ushort actorLevel, uint difficulty,
        SacredEquipmentGenerationData data, SacredRandomSequence random, SacredX87Precision precision)
    {
        if (item.ResourceCategory == SacredItemCategory.Weapon)
        { WorldScriptEquipmentGeneration.Apply(item,actorLevel,difficulty,data,random,precision); return; }
        if (item.ResourceCategory != SacredItemCategory.Shield || item.InitialValuesPrepared || item.SavedState is not null ||
            !item.Node.Active || item.Physical.AttachmentParent != 0 || (item.Physical.Base.Flags & 2) == 0 ||
            !item.Values.AsSpan().SequenceEqual(data.Templates.GetInstance(item.Physical.Base.ObjectType)))
            throw new NotSupportedException("CreateNPC original Shield values require the actual fresh registered constructor and original template.");
        item.OriginalScriptValuesPrepared = true;
    }
}
