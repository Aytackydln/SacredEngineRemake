using System.Numerics;

namespace Sacred.Particles;

/// <summary>Native helper endpoints, in the effect object's local coordinate space.</summary>
public readonly record struct SacredFxBeamSegment(Vector3 Start, Vector3 End);
