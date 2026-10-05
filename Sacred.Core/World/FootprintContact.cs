using System.Numerics;

namespace Sacred.Core.World;

public readonly record struct FootprintContact(Vector2 Position, Vector2 Direction,
    TerrainFootprintKind Kind, bool RightFoot, float AgeSeconds)
{
    public float LifetimeSeconds => Kind == TerrainFootprintKind.Water
        ? TerrainFootprintTextures.FrameCount(Kind) * FootprintTrail.WaterFrameSeconds
        : FootprintTrail.TrackLifetimeSeconds;
    public int Frame => Kind == TerrainFootprintKind.Water
        ? Math.Min(TerrainFootprintTextures.FrameCount(Kind) - 1,
            (int)(AgeSeconds / FootprintTrail.WaterFrameSeconds)) : 0;
    public float Opacity => Kind == TerrainFootprintKind.Water ? 1f
        : Math.Clamp((LifetimeSeconds - AgeSeconds) / 2f, 0f, 1f);
}
