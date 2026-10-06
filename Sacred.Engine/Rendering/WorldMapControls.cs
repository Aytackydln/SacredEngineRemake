using Sacred.World.Map;

namespace Sacred.Engine.Rendering;

public sealed class WorldMapControls
{
    public WorldMapKind SelectedMap { get; set; }
    public bool RegionNamesVisible { get; set; } = true;
    public bool RegionNpcsVisible { get; set; } = true;
    public bool FitRequested { get; set; }
}
