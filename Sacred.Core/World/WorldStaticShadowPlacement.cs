namespace Sacred.Core.World;

/// <summary>Where the native static-object queue builder schedules an atlas shadow.</summary>
public enum WorldStaticShadowPlacement
{
    None,
    /// <summary>A separate entry in WorldRenderLayer.Shadows.</summary>
    ShadowsQueue,
    /// <summary>Draw immediately before this sprite in WorldRenderLayer.Objects.</summary>
    BeforeObject,
}
