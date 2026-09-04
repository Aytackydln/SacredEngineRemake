using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using Sacred.Core.World.Sector;
using Sacred.Engine.Rendering;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;
using DearImGui = ImGuiNET.ImGui;

namespace Sacred.Engine.Graphics.ImGui;

/// <summary>Builds the categorized engine panel and screen-space world diagnostics.</summary>
internal sealed class ImGuiDebugPanel(
    Dx12ImGuiRenderer renderer,
    TerrainRenderer terrain,
    Dx12DeviceContext graphics,
    DebugUiControlState controls)
{
    private static readonly Vector4 PropertyColour = new(0.94f, 0.55f, 0.20f, 0.95f);
    private static readonly Vector4 EntranceColour = new(1.00f, 1.00f, 1.00f, 0.95f);

    public void Build(
        SacredCamera camera,
        VisibleWorld world,
        SceneState scene,
        Dx12DebugOverlayStats rendererStats,
        IReadOnlyList<TerrainStaticSprite> staticSprites,
        IReadOnlyList<TerrainWorldLight> worldLights,
        double framesPerSecond,
        int renderWidth,
        int renderHeight)
    {
        if (!scene.Debug.OverlaysVisible)
            return;

        DrawToggle(scene.Debug);
        if (scene.Debug.PanelVisible)
            DrawPanel(camera, world, scene, rendererStats, framesPerSecond);

        ImGuiWorldDebugRenderer.Draw(
            camera,
            world,
            scene.Debug,
            staticSprites,
            worldLights,
            renderWidth,
            renderHeight);
    }

    private static void DrawToggle(SceneDebugState debug)
    {
        DearImGui.SetNextWindowPos(new Vector2(12.0f, 70.0f), ImGuiCond.Always);
        DearImGui.SetNextWindowBgAlpha(0.88f);
        var flags = ImGuiWindowFlags.NoDecoration |
                    ImGuiWindowFlags.AlwaysAutoResize |
                    ImGuiWindowFlags.NoSavedSettings |
                    ImGuiWindowFlags.NoNav;
        DearImGui.Begin("##debug-toggle", flags);
        if (DearImGui.Button(debug.PanelVisible ? "Hide debug panel" : "Open debug panel"))
        {
            debug.PanelVisible = !debug.PanelVisible;
            EngineLog.WriteLine($"Debug input: ImGui panel {(debug.PanelVisible ? "opened" : "closed")}");
        }
        DearImGui.End();
    }

    private void DrawPanel(
        SacredCamera camera,
        VisibleWorld world,
        SceneState scene,
        Dx12DebugOverlayStats rendererStats,
        double framesPerSecond)
    {
        DearImGui.SetNextWindowSize(new Vector2(560.0f, 720.0f), ImGuiCond.FirstUseEver);
        DearImGui.SetNextWindowPos(new Vector2(12.0f, 120.0f), ImGuiCond.FirstUseEver);
        var open = scene.Debug.PanelVisible;
        DearImGui.PushFont(renderer.TitleFont);
        var drawContents = DearImGui.Begin("Sacred Engine Debug", ref open);
        DearImGui.PopFont();
        scene.Debug.PanelVisible = open;
        if (!drawContents)
        {
            DearImGui.End();
            return;
        }

        DearImGui.PushFont(renderer.BodyFont);
        if (DearImGui.CollapsingHeader("Cheats", ImGuiTreeNodeFlags.DefaultOpen))
            ImGuiCheatsPanel.Draw(controls);
        if (DearImGui.CollapsingHeader("Settings", ImGuiTreeNodeFlags.DefaultOpen))
            ImGuiSettingsPanel.Draw(graphics, controls);
        if (DearImGui.CollapsingHeader("Performance", ImGuiTreeNodeFlags.DefaultOpen))
            DrawPerformance(rendererStats, framesPerSecond);
        if (DearImGui.CollapsingHeader("World & streaming", ImGuiTreeNodeFlags.DefaultOpen))
            DrawWorld(camera, world, scene);
        if (DearImGui.CollapsingHeader("Rendering", ImGuiTreeNodeFlags.DefaultOpen))
            DrawRendering(scene, rendererStats);
        if (DearImGui.CollapsingHeader("Tile visualizers", ImGuiTreeNodeFlags.DefaultOpen))
            DrawTileVisualizers(scene.Debug);
        if (DearImGui.CollapsingHeader("Object visualizers", ImGuiTreeNodeFlags.DefaultOpen))
        {
            Checkbox("World light sources and bounds", scene.Debug.WorldLightBoundsVisible,
                value => scene.Debug.WorldLightBoundsVisible = value);
            Checkbox("Static sprite bounds and anchors", scene.Debug.StaticSpriteBoundsVisible,
                value => scene.Debug.StaticSpriteBoundsVisible = value);
            DearImGui.Separator();
            DearImGui.TextDisabled("Static.pak bytes 0x08-0x0B object flags");
            foreach (var option in WorldDebugFlagCatalog.StaticFlags)
            {
                FlagCheckbox(
                    option,
                    scene.Debug.VisibleStaticObjectFlags,
                    value => scene.Debug.VisibleStaticObjectFlags = value);
            }
        }
        if (DearImGui.CollapsingHeader("Controls"))
            DrawControls();
        DearImGui.PopFont();
        DearImGui.End();
    }

    private static void DrawPerformance(Dx12DebugOverlayStats stats, double fps)
    {
        DearImGui.Text($"Frame rate      {fps:0.0} FPS");
        DearImGui.Text($"Frame time      {stats.FrameTimeMilliseconds:0.00} ms");
        DearImGui.Text($"Pacing          {stats.FramePacingStatus}");
    }

    private static void DrawWorld(SacredCamera camera, VisibleWorld world, SceneState scene)
    {
        DearImGui.Text($"Camera          {camera.WorldCenter.X:0.00}, {camera.WorldCenter.Y:0.00}");
        DearImGui.Text($"Center sector   {world.CenterSector.X}, {world.CenterSector.Y}");
        DearImGui.Text($"Visible sectors {world.Sectors.Count} (loading {world.LoadingSectors})");
        DearImGui.Text($"Actor terrain Z {scene.Debug.ActorTerrainHeight:0.00}");
        DearImGui.Text($"Indoor group    {scene.Indoor.ActiveGroup?.Id.ToString() ?? "none"}");
        DearImGui.Text($"Active model    {FormatActiveModel(scene)}");
    }

    private void DrawRendering(SceneState scene, Dx12DebugOverlayStats stats)
    {
        var terrainStats = terrain.LastStats;
        DearImGui.Text($"Sector images   {terrainStats.SectorImagesDrawn}/{terrainStats.SectorImagesCached} (building {terrainStats.SectorImagesPending})");
        DearImGui.Text($"GPU sectors     {stats.GpuSectorTextureCount}/{stats.MaxSectorTextureCount} (uploading {stats.PendingSectorUploadCount})");
        DearImGui.Separator();
        DearImGui.Text($"Ground tiles    {terrainStats.DrawnTiles}/{terrainStats.CandidateTiles}  missing {terrainStats.MissingTiles}  cache {terrainStats.CachedTiles}");
        DearImGui.Text($"Floor tiles     {terrainStats.FloorDrawnTiles}/{terrainStats.FloorCandidateTiles}  missing {terrainStats.FloorMissingTiles}  cache {terrainStats.FloorCachedTiles}");
        DearImGui.Text($"Liquid sprites  {stats.VisibleLiquidSpriteCount}/{terrainStats.LiquidDrawnTiles}  candidates {terrainStats.LiquidCandidateTiles}");
        DearImGui.Text($"Static sprites  {stats.VisibleStaticSpriteCount}/{terrainStats.StaticDrawnObjects}  candidates {terrainStats.StaticCandidateObjects}  missing {terrainStats.StaticMissingObjects}");
        DearImGui.Separator();
        DearImGui.Text($"Model textures  ready {stats.ReadyModelTextureCount}  loading {stats.LoadingModelTextureCount}  uploading {stats.UploadingModelTextureCount}  failed {stats.FailedModelTextureCount}");
        DearImGui.Text($"Static shadows  {stats.VisibleStaticShadowCount}  draws {stats.StaticShadowDrawCallCount}  legacy {stats.LegacyShadowDrawCallCount}");
        DearImGui.Text($"Lights/halos    {stats.VisibleHaloCount} visible  {stats.CandidateHaloCount} candidates  {stats.SurfaceLightCount} surface lights");
        DearImGui.Text($"Night blend     {scene.Lighting.NightBlend:0.000}  sun height {scene.Lighting.SunHeight:0.000}");
    }

    private static void DrawTileVisualizers(SceneDebugState debug)
    {
        DearImGui.TextDisabled("Geometry and navigation");
        Checkbox("Tile tessellation / vertices", debug.TerrainTopologyVisible,
            value => debug.TerrainTopologyVisible = value);
        Checkbox("World tile coordinates", debug.TileCoordinatesVisible,
            value => debug.TileCoordinatesVisible = value);
        Checkbox("Blocked movement (F9)", debug.BlockedAreasVisible,
            value => debug.BlockedAreasVisible = value);
        Checkbox("Stairs and indoor doors (F8)", debug.StairsMapVisible,
            value => debug.StairsMapVisible = value);
        Checkbox("Sector bounds", debug.SectorBoundsVisible,
            value => debug.SectorBoundsVisible = value);

        DearImGui.TextDisabled("KEYX byte 0x1CC sector flags");
        foreach (var option in WorldDebugFlagCatalog.SectorFlags)
            FlagCheckbox(option, debug.VisibleSectorFlags, value => debug.VisibleSectorFlags = value);

        DearImGui.Separator();
        DearImGui.TextDisabled("WLDX byte 0x1E path flags");
        foreach (var option in WorldDebugFlagCatalog.PathFlags)
            FlagCheckbox(option, debug.VisiblePathFlags, value => debug.VisiblePathFlags = value);

        DearImGui.Separator();
        DearImGui.TextDisabled("WLDX byte 0x1F low-nibble flags");
        foreach (var option in WorldDebugFlagCatalog.TileFlags)
            FlagCheckbox(option, debug.VisibleTileFlags, value => debug.VisibleTileFlags = value);

        DearImGui.TextDisabled("WLDX byte 0x1F high-nibble surface flags");
        foreach (var option in WorldDebugFlagCatalog.SurfaceFlags)
            FlagCheckbox(option, debug.VisibleSurfaceFlags, value => debug.VisibleSurfaceFlags = value);

        DearImGui.TextDisabled("WLDX byte 0x1F interpreted values");
        ColouredCheckbox("Exact movement-blocker values", PropertyColour, debug.MovementFlagTilesVisible,
            value => debug.MovementFlagTilesVisible = value);
        ColouredCheckbox("Entrance / exterior boundary", EntranceColour, debug.EntranceTilesVisible,
            value => debug.EntranceTilesVisible = value);
        Checkbox("Terrain-surface values", debug.TerrainSurfacesVisible,
            value => debug.TerrainSurfacesVisible = value);

        DearImGui.Separator();
        DearImGui.TextDisabled("WLDX terrain samples");
        Checkbox("Visual elevation (0x10-0x13)", debug.VisualElevationVisible,
            value => debug.VisualElevationVisible = value);
        Checkbox("Gameplay elevation (0x18-0x1B)", debug.GameplayElevationVisible,
            value => debug.GameplayElevationVisible = value);
        Checkbox("Baked brightness (0x14-0x17)", debug.BakedLightingVisible,
            value => debug.BakedLightingVisible = value);
    }

    private static void DrawControls()
    {
        DearImGui.Text("Move             WASD / arrows / left stick");
        DearImGui.Text("Cycle character  Mouse 4/5 / gamepad B");
        DearImGui.Text("Minimap          Hold Tab / middle mouse / Select");
        DearImGui.Text("World map        M / tap Select");
        DearImGui.Text("HDR              F4");
        DearImGui.Text("Frame pacing     F5");
        DearImGui.Text("Low latency      F6");
        DearImGui.Text("World light      F7");
        DearImGui.Text("Stairs zones     F8");
        DearImGui.Text("Blocked tiles    F9");
        DearImGui.Text("Fullscreen       F10");
        DearImGui.Text("Screenshot       F12");
    }

    private static void Checkbox(string label, bool current, Action<bool> setter)
    {
        if (DearImGui.Checkbox(label, ref current))
        {
            setter(current);
            EngineLog.WriteLine($"Debug input: {label} {(current ? "enabled" : "disabled")}");
        }
    }

    private static void ColouredCheckbox(string label, Vector4 colour, bool current, Action<bool> setter)
    {
        DearImGui.PushStyleColor(ImGuiCol.CheckMark, colour);
        Checkbox(label, current, setter);
        DearImGui.PopStyleColor();
    }

    private static void FlagCheckbox<T>(
        WorldDebugFlagOption<T> option,
        T currentFlags,
        Action<T> setter)
        where T : struct, Enum
    {
        var enabled = currentFlags.HasFlag(option.Flag);
        ColouredCheckbox(
            option.Label,
            option.Colour,
            enabled,
            value => setter(SetFlag(currentFlags, option.Flag, value)));
    }

    private static T SetFlag<T>(T currentFlags, T flag, bool enabled)
        where T : struct, Enum
    {
        var current = Convert.ToUInt64(currentFlags);
        var value = Convert.ToUInt64(flag);
        var updated = enabled ? current | value : current & ~value;
        return (T)Enum.ToObject(typeof(T), updated);
    }

    private static string FormatActiveModel(SceneState scene)
    {
        if (scene.Models.Count == 0)
            return "none";
        var model = scene.Models[0];
        return $"{model.Name}  V{model.Mesh.Vertices.Length} I{model.Mesh.Indices.Length}";
    }

}
