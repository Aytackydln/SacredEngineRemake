using System.Numerics;
using Sacred.Granny.Animation;

namespace Sacred.Inventory.Effects;

public readonly record struct EquipmentEffectHandle(long Identity, uint Generation);
public enum EquipmentEffectOperation { Pose, Advance, Rebase, Reset }

/// <summary>Captures the requested source pose and transforms, never mutable palette spans.
/// PoseSource supplies immutable mesh/skin identity; its current pose is not the packet's version.</summary>
public readonly record struct EquipmentEffectRequest(
    EquipmentEffectHandle Instance, ulong Sequence, EquipmentEffectOperation Operation,
    GrnAnimatedMesh? PoseSource, GrnPoseRequest? PoseRequest, float DeltaSeconds,
    Matrix4x4 PreviousWorld, Matrix4x4 CurrentWorld);

public interface IEquipmentEffectRequestSink
{
    void Submit(in EquipmentEffectRequest request);
}
