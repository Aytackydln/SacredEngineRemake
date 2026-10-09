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
        IsReadyForLoad(waitForSectorGpuUploads: true);

    public bool IsReadyForLoad(bool waitForSectorGpuUploads) =>
        SectorsLoaded &&
        SectorImagesBuilt &&
        (!waitForSectorGpuUploads || SectorImagesUploaded) &&
        SpriteAssetsLoaded &&
        SpriteTexturesUploaded &&
        ModelGeometryPrepared;

    public string PendingItem => GetPendingItem(waitForSectorGpuUploads: true);

    public string GetPendingItem(bool waitForSectorGpuUploads)
    {
        if (!SectorsLoaded) return "Loading sectors";
        if (!SectorImagesBuilt) return "Building sector images";
        if (!SpriteAssetsLoaded) return "Loading static objects";
        if (waitForSectorGpuUploads && !SectorImagesUploaded) return "Uploading sectors to GPU";
        if (!SpriteTexturesUploaded) return "Uploading static objects to GPU";
        if (!ModelGeometryPrepared) return "Preparing model geometry";
        return "World ready";
    }
}
