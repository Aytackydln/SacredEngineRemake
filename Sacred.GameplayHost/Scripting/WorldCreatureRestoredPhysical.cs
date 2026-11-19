using System.Numerics;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Physical file-import state after Gold 44FC50. This is not a network
/// packet and does not imply creature-value normalization or world placement.</summary>
public sealed record WorldCreatureRestoredPhysical(uint Animation, uint WordF0, uint Word60,
    uint Word68, uint Word6C, uint PaletteMask, GoldPhysicalVector Velocity,
    GoldPhysicalVector AngularVelocity, GoldPhysicalVector ExactPosition,
    GoldPhysicalVector BoundsMinimum, GoldPhysicalVector BoundsMaximum,
    short QuestNumber, short QuestEffect, uint Flags, GoldPhysicalVector BonePosition,
    Vector3 Rotation, Vector3 RootOffset, Matrix4x4? ModelOrientation,
    float LoopCount, double PlaybackSpeed);
