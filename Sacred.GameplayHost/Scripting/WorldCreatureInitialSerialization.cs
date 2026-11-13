using System.Buffers.Binary;
using Sacred.Core.Pak.Items;
using Sacred.GameplayHost.Equipment;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Initial ordinary NPC 0x16F stream. The zero-handle bone output is
/// canonicalized to zero: placed creature Reset3DPosition bypasses that cache
/// (0x44D582/0x426610). This policy does not extend to non-creature resources.</summary>
public static class WorldCreatureInitialSerialization
{
    public static GoldCreatureState Write(WorldCreatureInitialActor actor, ItemsPakEntry descriptor,
        IReadOnlyDictionary<uint, WorldEquipmentInitialItem> registry, Func<uint, ItemsPakEntry> itemDescriptor)
    {
        if (descriptor.ItemIndex != actor.Base.ObjectType || descriptor.ModelDesc.Category != SacredItemCategory.Creature ||
            actor.Base.ObjectReference <= 16 || actor.KiMode != 0 || actor.EiMode != 0 ||
            actor.Component.ResultValid > 1 || (actor.Base.Flags & (GoldPhysicalStateLayout.ModelLoadedFlag | GoldPhysicalStateLayout.VectorCCFlag)) != 0)
            throw new NotSupportedException("Initial NPC serialization requires its resolved ordinary unloaded creature and final network result state.");
        var physical = GoldPhysicalConstructionWriter.Write(actor.Base, actor.Direction);
        var values = GoldCreatureValuesWriter.Write(0x16f, actor.Component.Values, actor.Component.Health);
        var bytes = GoldCreatureConstructionWriter.Write(physical, values, actor.RelationClass,
            actor.BehaviorFlags, actor.EquipmentSelector, actor.RandomTail).ToPayload();
        for (var slot = 0; slot < actor.EquipmentReferences.Length; slot++)
        {
            var reference = actor.EquipmentReferences[slot];
            if (reference == 0) continue;
            if (!registry.TryGetValue(reference, out var item) || item.Physical.AttachmentParent != actor.Base.ObjectReference)
                throw new InvalidDataException("Initial NPC stream requires its registered installed equipment graph.");
            // 0x54BD18/0x5FE790 suppress descriptor fallback items, not arbitrary
            // missing references. Initial graph completeness is checked above.
            if ((itemDescriptor(item.Physical.Base.ObjectType).ModelDesc.GraphicFlags & SacredItemGraphicFlags.EquipmentFallback) != 0) continue;
            Word(GoldCreatureStateLayout.EquipmentOffset + slot * 4, reference);
        }
        bytes[GoldCreatureStateLayout.Byte2B6Offset] = actor.SpellSelector;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.Word261Offset), actor.SpellParameter);
        Word(GoldCreatureStateLayout.Word200Offset, actor.RuntimeFlags);
        Word(GoldCreatureStateLayout.Word245Offset, actor.DialogIndex);
        Word(GoldCreatureStateLayout.ScriptGroupOffset, actor.ScriptGroup);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.Word2B7Offset), actor.Flags2B7);
        var position = actor.ScriptInitialPosition;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.ScriptInitialPositionOffset), checked((ushort)position.Sector));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.ScriptInitialPositionOffset + 4), position.X);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.ScriptInitialPositionOffset + 8), position.Y);
        bytes[GoldCreatureStateLayout.ScriptInitialPositionOffset + 12] = checked((byte)position.Surface);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(bytes.Length - GoldCreatureTailLayout.TrailerSize + GoldCreatureTailLayout.MountPartnerReferenceOffset), actor.PartnerReference);
        actor.ActionState.CopyTo(bytes, GoldCreatureStateLayout.Block2EDOffset);
        return GoldCreatureState.FromPayload(bytes);
        void Word(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
    }
}
