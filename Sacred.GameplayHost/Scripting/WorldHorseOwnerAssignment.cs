using System.Buffers.Binary;
using Sacred.Assets.Executable;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;
namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x462624..0x46271F for a fresh unmounted Horse and nonplayer owner.
/// Reciprocal prior Horse detachment is supported; populated merchant stock and mounted/equipped state remain separate.</summary>
public static class WorldHorseOwnerAssignment
{
    public static (WorldCreatureInitialActor Horse, WorldCreatureInitialActor Owner, WorldCreatureInitialActor Previous) ApplyReplacing(
        WorldCreatureInitialActor horse, WorldCreatureInitialActor owner, WorldCreatureInitialActor previous,
        SacredHorseValueBalance values, SacredHorseOwnerDefinitions definitions, SacredX87Precision precision)
    {
        if (previous.RelationClass != (uint)SacredCreatureClass.Horse || previous.PartnerReference != owner.Base.ObjectReference ||
            owner.PartnerReference != previous.Base.ObjectReference || previous.EquipmentReferences.Any(value => value != 0) || previous.Node.Active)
            throw new NotSupportedException("SetOwner replacement requires the compared dormant reciprocal previous Horse without equipment.");
        var detached = previous with { PartnerReference = 0, Base = previous.Base with { Flags = previous.Base.Flags | Sacred.Network.Gameplay.GoldCreatureMountLayout.DetachSetMask } };
        var (next,nextOwner) = Apply(horse,owner with { PartnerReference = 0 },values,definitions,precision);
        return (next,nextOwner,detached);
    }
    public static (WorldCreatureInitialActor Horse, WorldCreatureInitialActor Owner) Apply(
        WorldCreatureInitialActor horse, WorldCreatureInitialActor owner, SacredHorseValueBalance values,
        SacredHorseOwnerDefinitions definitions, SacredX87Precision precision)
    {
        if (horse.RelationClass != (uint)SacredCreatureClass.Horse || owner.RelationClass == (uint)SacredCreatureClass.Horse ||
            horse.Base.ObjectReference <= 16 || owner.Base.ObjectReference <= 16 || horse.Node.Active || owner.Node.Active ||
            horse.EquipmentReferences.Any(value => value != 0) || owner.EquipmentReferences.Any(value => value != 0) ||
            horse.PartnerReference != 0 || owner.PartnerReference != 0)
            throw new NotSupportedException("SetOwner requires the compared dormant empty-equipment Horse/owner pair without prior partners.");
        var buffer = (byte[])horse.Component.Values.Clone();
        buffer[80] = (byte)Math.Clamp((int)buffer[80],1,9);
        var level = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(78));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(62), values.OwnerValue(level,buffer[80],precision));
        var next = horse with
        {
            Base = horse.Base with { Flags = horse.Base.Flags | definitions.DialogFlags },
            DialogIndex = (owner.BehaviorFlags & 0x4000) != 0 ? definitions.SpecialDialog : definitions.NormalDialog,
            PartnerReference = owner.Base.ObjectReference,
            Component = new(buffer,(byte[])horse.Component.Health.Clone(),horse.Component.CombatProfile,1)
        };
        return (next, owner with { PartnerReference = horse.Base.ObjectReference });
    }
}
