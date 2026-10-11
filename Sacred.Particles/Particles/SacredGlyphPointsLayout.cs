using System.Numerics;
using System.Runtime.CompilerServices;

namespace Sacred.Particles.Particles;

/// <summary>Twenty native waves, each with nine consecutive XYZ points.</summary>
[InlineArray(180)]
public struct SacredGlyphPointsLayout
{
    private Vector3 _first;
}
