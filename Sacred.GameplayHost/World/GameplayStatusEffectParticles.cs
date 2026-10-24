using System.Numerics;
using Sacred.Network.Gameplay;
using Sacred.World.Networking;
using Sacred.World.Particles;

namespace Sacred.GameplayHost.World;

/// <summary>Shared received-action placement; fixed ground casts do not require actor model assets.</summary>
public static class GameplayStatusEffectParticles
{
    public static WorldAttachedParticleEffect? Create(GameplayStatusEffect effect,
        Func<uint, string, WorldObjectFxAttachment?> attachment, Func<GoldObjectPosition, float> groundHeight)
    {
        if (!effect.VisualAvailable) return null;
        Vector2 point;
        float height;
        uint? radius = null;
        if (effect.GroundTarget is { } ground)
        {
            point = new Vector2(ground.X, ground.Y) / SacredNetworkPosition.NativeUnitsPerTile;
            height = groundHeight(ground);
        }
        else
        {
            var anchor = attachment(effect.Owner, effect.Bone);
            if (anchor is null || anchor.InstanceVersion != effect.OwnerInstance) return null;
            (point, height, radius) = (anchor.Position, anchor.Height, anchor.BlockRadius);
        }
        return new(effect.Generation, effect.Type, effect.Preset, point, height, effect.Age)
        { DurationSeconds = float.IsFinite(effect.Duration) ? effect.Duration : null, ActorBlockRadius = radius,
            Direction = effect.Definition.Direction };
    }
}
