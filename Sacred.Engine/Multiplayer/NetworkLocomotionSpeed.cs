using Sacred.Core.Pak.Creature;
using Sacred.Inventory.Stats;

namespace Sacred.Engine.Multiplayer;

/// <summary>Native locomotion rate before the selected Models.tmp motion multiplier.</summary>
internal static class NetworkLocomotionSpeed
{
    public static float Resolve(SacredMovementCalculator? calculator, short? receivedPercent,
        float actorFactor, SacredCreatureTemplate? template, uint behaviorFlags, uint action, int pathSpeed)
    {
        if (calculator is not null && receivedPercent is { } percent)
            return calculator.AnimationPlaybackSpeed(percent, actorFactor);

        // Compact values omit calculated movement percentage. Their path bank
        // retains speed after actor/global factors; dividing by the authored base
        // rate recovers the clock to within native integer-speed truncation.
        // Do not multiply actor/global factors into this ratio a second time.
        if (template is not null && pathSpeed > 0)
        {
            var baseSpeed = (template.Flags & 1) != 0 && (behaviorFlags & 0x200000) != 0
                ? 200 : action == 1 ? template.WalkSpeed : template.RunSpeed;
            if (baseSpeed > 0) return (float)pathSpeed / baseSpeed;
        }
        return actorFactor * (calculator?.GlobalAnimationScale ?? 1);
    }
}
