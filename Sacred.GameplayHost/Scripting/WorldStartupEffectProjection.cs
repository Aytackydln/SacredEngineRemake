using System.Numerics;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Runtime;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Explicit, compared Gold renderer inputs for the headless startup
/// profile. These match the retained native projection context; other camera
/// profiles must supply and compare their own initialized matrices.</summary>
public static class WorldStartupEffectProjection
{
    public static SacredObjectProjection ComparedGold { get; } = new(
        new Matrix4x4(1,0,0,0, 0,-.4472135901451111f,-.8944271802902222f,0,
            0,.8944271802902222f,-.4472135901451111f,0, 0,0,0,1),
        new Matrix4x4(.00374531839042902f,0,0,0, 0,.004999999888241291f,0,0,
            0,0,.0003225806576665491f,0, 0,0,.19354838132858276f,1));
}
