using System.Buffers.Binary;
using System.Numerics;
using Sacred.Core.Pax.Hero.Objects;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Runtime;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 449AA0/44FC50 supported active hero physical stage. Activation
/// precedes saved creature/equipment import, so constructor state controls it.
/// The caller supplies a successfully opened original model/sequence, never a
/// fabricated success handle. The original factory still owns the node.</summary>
public static class WorldCreatureSavedPhysicalImport
{
    public static WorldCreatureInitialActor Restore(WorldCreatureInitialActor initial, ReadOnlySpan<byte> source,
        Vector3? loadedModelRoot, float motionSpeed, float globalSpeed, SacredObjectProjection projection)
    {
        if (source.Length != SacredSavedPhysicalLayout.Size || initial.RestoredPhysical is not null)
            throw new InvalidOperationException("Physical import requires one complete saved prefix and an unrestored actor.");
        var saved = System.Runtime.InteropServices.MemoryMarshal.Read<SacredSavedObjectBaseLayout>(source);
        var request = SacredSavedAnimationRequest.Read(source);
        if (!initial.Node.Active || initial.Base.ObjectReference is 0 or > 16 || initial.RelationClass != (uint)SacredCreatureClass.Hero ||
            saved.ObjectReference != initial.Base.ObjectReference ||
            saved.Resource != initial.Base.ObjectType || saved.Owner != initial.Base.Owner ||
            initial.EquipmentReferences.Any(value => value != 0) || initial.KiMode != 0 || initial.EiMode != 0 ||
            initial.Component.Health.Length != 24 || BinaryPrimitives.ReadInt32LittleEndian(initial.Component.Health) <= 0)
            throw new NotSupportedException("Saved physical import requires the live initial local hero identity, health and empty constructor equipment/actions; outer identity rebasing is still pending for other slots.");
        if (request.RequiresActivation && loadedModelRoot is null)
            throw new InvalidOperationException("Nonzero saved animation requires its successful original model and sequence backend.");
        if (!float.IsFinite(request.FirstArgument) || !float.IsFinite(request.SecondArgument) ||
            !float.IsFinite(motionSpeed) || !float.IsFinite(globalSpeed))
            throw new NotSupportedException("Saved animation requires finite compared control inputs.");
        var flags = saved.Flags & ~0x04001000u; // 449C56 discards loaded-model and transient constructor bits.
        if ((flags & 2) == 0) flags |= 0x400000;
        var physicalFlags = BinaryPrimitives.ReadUInt32LittleEndian(source[SacredSavedPhysicalLayout.WordB0Offset..]) | 4;
        if ((flags & 0x800000) != 0) { physicalFlags &= ~4u; flags &= ~0x200u; }
        var basis = new GoldObjectBaseState(saved.ObjectReference, saved.Resource, flags, saved.Sector,
            saved.X, saved.Y, saved.Surface, saved.Word28, saved.Word34, saved.Byte38, saved.Owner, saved.Word3C);
        var direction = Vector(source, SacredSavedPhysicalLayout.Vector70Offset);
        Matrix4x4? orientation = null;
        var loops = request.FirstArgument;
        double speed = request.SecondArgument;
        if (request.RequiresActivation)
        {
            basis = basis with { Flags = (basis.Flags & ~0x9000u) | 0x04000200u };
            orientation = WorldCreatureModelOrientation.Create(basis, direction, projection);
            // 4500F8 clears the old control's flag, then 44A894 sets it for the new control.
            loops = Math.Min(10000, loops);
            var arithmetic = new SacredResultArithmetic(SacredX87Precision.Single);
            var actorSpeed = (float)arithmetic.Multiply(request.SecondArgument, initial.AnimationSpeedMultiplier);
            var authored = motionSpeed is >= .8f and < 5f ? motionSpeed : 1;
            speed = arithmetic.Multiply(globalSpeed, arithmetic.Multiply(authored, actorSpeed));
            basis = basis with { Flags = basis.Flags | 0x8000u };
        }
        else basis = basis with { Flags = basis.Flags & ~0x8000u };
        // Base reader always clears the dynamic next-on-tile link. The final
        // 36 saved bone bytes and four reserved dwords are consumed/discarded.
        return initial with
        {
            Base = basis, Direction = direction, NextOnTile = 0, StaticSurfaceReference = saved.WorldLink,
            RestoredPhysical = new(request.Selection, Word(source, SacredSavedPhysicalLayout.WordF0Offset), Word(source, SacredSavedPhysicalLayout.Word60Offset),
                Word(source, SacredSavedPhysicalLayout.Word68Offset), Word(source, SacredSavedPhysicalLayout.Word6COffset), Word(source, SacredSavedPhysicalLayout.Word5COffset),
                Vector(source, SacredSavedPhysicalLayout.VectorB4Offset), Vector(source, SacredSavedPhysicalLayout.VectorC0Offset), Vector(source, SacredSavedPhysicalLayout.VectorCCOffset),
                Vector(source, SacredSavedPhysicalLayout.BoundsMinimumOffset), Vector(source, SacredSavedPhysicalLayout.BoundsMaximumOffset),
                Short(source, SacredSavedPhysicalLayout.Word94Offset), Short(source, SacredSavedPhysicalLayout.Word96Offset), physicalFlags, Vector(source, SacredSavedPhysicalLayout.BoneVectorOffset),
                Vector3.Zero, loadedModelRoot ?? Vector3.Zero, orientation, loops, speed)
        };
    }

    private static uint Word(ReadOnlySpan<byte> source, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(source[offset..]);
    private static short Short(ReadOnlySpan<byte> source, int offset) => BinaryPrimitives.ReadInt16LittleEndian(source[offset..]);
    private static GoldPhysicalVector Vector(ReadOnlySpan<byte> source, int offset) => new(
        BinaryPrimitives.ReadSingleLittleEndian(source[offset..]), BinaryPrimitives.ReadSingleLittleEndian(source[(offset + 4)..]), BinaryPrimitives.ReadSingleLittleEndian(source[(offset + 8)..]));
}
