using Sacred.Core.Runtime;
using Sacred.GameplayHost.Runtime;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x4833EF..0x483426: retain the placed logical position at
/// native +0x2B9, then apply the script heading with 0x44DDC0.</summary>
public static class WorldCreatureScriptPose
{
    public static WorldCreatureInitialActor Apply(WorldCreatureInitialActor actor, int facingDegrees)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var direction = SacredStartupHeading.ToDirection(facingDegrees);
        return actor with { ScriptInitialPosition = new(actor.Base.X, actor.Base.Y, actor.Base.Surface, actor.Base.Sector),
            Direction = new(direction.X, direction.Y, direction.Z) };
    }
}
