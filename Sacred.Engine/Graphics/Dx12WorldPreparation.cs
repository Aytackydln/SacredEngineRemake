using Sacred.Core.World.Sector;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;

namespace Sacred.Engine.Graphics;

public sealed record WorldPreloadRequest(
    SacredCamera Camera,
    VisibleWorld World,
    SceneState Scene);

public readonly record struct WorldPreparationStatus(
    bool SectorsLoaded,
    bool SectorImagesBuilt,
    bool SectorImagesUploaded,
    bool SpriteAssetsLoaded,
    bool SpriteTexturesUploaded,
    bool ModelGeometryPrepared)
{
    public static WorldPreparationStatus NotStarted => new(false, false, false, false, false, false);

    public bool IsReady =>
        SectorsLoaded &&
        SectorImagesBuilt &&
        SectorImagesUploaded &&
        SpriteAssetsLoaded &&
        SpriteTexturesUploaded &&
        ModelGeometryPrepared;

    public string PendingItem
    {
        get
        {
            if (!SectorsLoaded) return "Loading sectors";
            if (!SectorImagesBuilt) return "Building sector images";
            if (!SpriteAssetsLoaded) return "Loading static objects";
            if (!SectorImagesUploaded) return "Uploading sectors to GPU";
            if (!SpriteTexturesUploaded) return "Uploading static objects to GPU";
            if (!ModelGeometryPrepared) return "Preparing model geometry";
            return "World ready";
        }
    }
}
