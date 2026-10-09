using Sacred.World.Map;

namespace Sacred.Engine.Rendering;

public sealed class WorldMapControls
{
    public bool DebugPanelVisible { get; set; }
    public bool MinimapVisible { get; set; }
    public WorldMapKind SelectedMap { get; set; }
    public bool RegionNamesVisible { get; set; } = true;
    public bool RegionNpcsVisible { get; set; } = true;
    public bool FitRequested { get; set; }
}
