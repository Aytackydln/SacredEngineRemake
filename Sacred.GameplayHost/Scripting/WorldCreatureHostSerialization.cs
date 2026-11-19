using System.Buffers.Binary;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pax.Hero.Objects;
using Sacred.GameplayHost.Equipment;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Actual restored host hero 0x169, after native entry/metadata and
/// local-value validation. Uses runtime fields/lists, retaining only unchanged
/// saved words. It does not publish, create replicas or grant readiness.</summary>
public static class WorldCreatureHostSerialization
{
    public static GoldCreatureState Write(WorldCreatureInitialActor actor,
        IReadOnlyDictionary<uint, WorldEquipmentInitialItem> items, Func<uint, ItemsPakEntry> descriptor, byte mode)
    {
        if (!actor.CampaignEntryPlaced || !actor.ModelReactivated || !actor.SavedWorldPreludeCompleted ||
            actor.Base.ObjectReference is 0 or > 16 || actor.Base.Owner != actor.Base.ObjectReference || !actor.Node.Active ||
            actor.RestoredCreature is not { } saved || actor.RestoredPhysical is not { } physical ||
            actor.KiMode != 0 || actor.EiMode != 0 || actor.PartnerReference != 0 || saved.HasExtraLists ||
            saved.StatusModifiers.Count != 0 || mode > 2 || (actor.Base.Flags & GoldPhysicalStateLayout.VectorCCFlag) != 0 || (physical.Flags & 4) == 0)
            throw new NotSupportedException("Host serialization requires its actual entered healthy-model graph, current retained physical vector and supported empty status/extra/mount state.");
        var values = GoldCreatureValuesWriter.Write(0x169, actor.Component.Values, actor.Component.Health,
            saved.CombatArts.SelectMany(record => record).ToArray(), saved.Combos.SelectMany(record => record).ToArray(), actor.Component.Flags148).ToPayload();
        var result = new byte[GoldCreatureStateLayout.PrefixSize + values.Length + 13];
        var basis = actor.Base with { Flags = actor.Base.Flags & ~0x10000u };
        var physicalBytes = GoldPhysicalConstructionWriter.Write(basis, actor.Direction).ToPayload();
        Word(physicalBytes, GoldPhysicalStateLayout.AnimationIdOffset, physical.Animation);
        Word(physicalBytes, GoldPhysicalStateLayout.WordF0Offset, physical.WordF0);
        Word(physicalBytes, GoldPhysicalStateLayout.Word60Offset, physical.Word60);
        Word(physicalBytes, GoldPhysicalStateLayout.Word68Offset, physical.Word68);
        Word(physicalBytes, GoldPhysicalStateLayout.Word6COffset, physical.Word6C);
        Vector(physicalBytes, GoldPhysicalStateLayout.BoundsMinimumOffset, physical.BoundsMinimum);
        Vector(physicalBytes, GoldPhysicalStateLayout.BoundsMaximumOffset, physical.BoundsMaximum);
        Short(physicalBytes, GoldPhysicalStateLayout.QuestNumberOffset, unchecked((ushort)physical.QuestNumber));
        Short(physicalBytes, GoldPhysicalStateLayout.QuestEffectOffset, unchecked((ushort)physical.QuestEffect));
        Word(physicalBytes, GoldPhysicalStateLayout.WordB0Offset, physical.Flags);
        Vector(physicalBytes, GoldPhysicalStateLayout.SelectedVectorOffset, physical.BonePosition);
        physicalBytes.CopyTo(result, 0);
        Word(result, GoldCreatureStateLayout.MarkerOffset, GoldCreatureStateLayout.Marker);
        for (var slot = 0; slot < actor.EquipmentReferences.Length; slot++)
        {
            var reference = actor.EquipmentReferences[slot];
            if (reference == 0) continue;
            if (!items.TryGetValue(reference, out var item) || !item.Node.Active || !item.SavedPostLoadCompleted ||
                item.Physical.Base.Owner != actor.Base.Owner || item.Physical.AttachmentParent != actor.Base.ObjectReference)
                throw new InvalidDataException("Hero export has an unresolved current equipment object.");
            if ((descriptor(item.Physical.Base.ObjectType).ModelDesc.GraphicFlags & SacredItemGraphicFlags.EquipmentFallback) == 0)
                Word(result, GoldCreatureStateLayout.EquipmentOffset + slot * 4, reference);
        }
        Word(result, GoldCreatureStateLayout.RelationClassOffset, actor.RelationClass);
        Word(result, GoldCreatureStateLayout.Word1F4Offset, actor.BehaviorFlags);
        Word(result, GoldCreatureStateLayout.LocomotionActionOffset, actor.LocomotionAction);
        Short(result, GoldCreatureStateLayout.Byte1FCOffset, actor.Flags1FC);
        Short(result, GoldCreatureStateLayout.Word1FEOffset, saved.Short(SacredSavedCreatureLayout.AuxiliaryPhysicalReferenceOffset));
        Word(result, GoldCreatureStateLayout.Word200Offset, actor.RuntimeFlags);
        Word(result, GoldCreatureStateLayout.Word245Offset, actor.DialogIndex);
        Word(result, GoldCreatureStateLayout.ScriptGroupOffset, actor.ScriptGroup);
        foreach (var (native, target) in new[] { (0x24d, GoldCreatureStateLayout.Word24DOffset), (0x251, GoldCreatureStateLayout.OwnerReferenceOffset),
                     (0x255, GoldCreatureStateLayout.Word255Offset), (0x259, GoldCreatureStateLayout.Word259Offset) })
            Word(result, target, saved.Word(SacredSavedCreatureLayout.Block1F0Offset + native - 0x1f0));
        Short(result, GoldCreatureStateLayout.Word25FOffset, actor.EquipmentSelector);
        Short(result, GoldCreatureStateLayout.Word261Offset, actor.SpellParameter);
        result[GoldCreatureStateLayout.Byte2B6Offset] = actor.SpellSelector;
        Short(result, GoldCreatureStateLayout.Word2B7Offset, actor.Flags2B7);
        var position = GoldCreatureStateLayout.ScriptInitialPositionOffset;
        for (var at = 0; at < GoldCreatureStateLayout.Block2B9Size; at += 4)
            Word(result, position + at, saved.Word(SacredSavedCreatureLayout.ScriptInitialPositionOffset + at));
        Short(result, position, checked((ushort)actor.ScriptInitialPosition.Sector));
        Word(result, position + 4, unchecked((uint)actor.ScriptInitialPosition.X));
        Word(result, position + 8, unchecked((uint)actor.ScriptInitialPosition.Y));
        result[position + 12] = checked((byte)actor.ScriptInitialPosition.Surface);
        Word(result, GoldCreatureStateLayout.AnimationSpeedMultiplierOffset, BitConverter.SingleToUInt32Bits(actor.AnimationSpeedMultiplier));
        actor.ActionState.CopyTo(result, GoldCreatureStateLayout.Block2EDOffset);
        values.CopyTo(result, GoldCreatureStateLayout.PrefixSize);
        var tail = GoldCreatureStateLayout.PrefixSize + values.Length + 2;
        result[tail] = mode;
        Word(result, tail + GoldCreatureTailLayout.MountPartnerReferenceOffset, actor.PartnerReference);
        Short(result, tail + GoldCreatureTailLayout.Word574Offset, actor.RandomTail);
        Word(result, tail + GoldCreatureTailLayout.Word578Offset, saved.Word(SacredSavedCreatureLayout.Word578Offset));
        return GoldCreatureState.FromPayload(result);
    }

    private static void Word(Span<byte> data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data[offset..], value);
    private static void Short(Span<byte> data, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(data[offset..], value);
    private static void Vector(Span<byte> data, int offset, GoldPhysicalVector value)
    {
        Word(data, offset, BitConverter.SingleToUInt32Bits(value.X));
        Word(data, offset + 4, BitConverter.SingleToUInt32Bits(value.Y));
        Word(data, offset + 8, BitConverter.SingleToUInt32Bits(value.Z));
    }
}
