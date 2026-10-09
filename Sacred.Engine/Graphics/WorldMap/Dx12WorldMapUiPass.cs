using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using ImGuiNET;
using Sacred.Engine.Assets;
using Sacred.Engine.Rendering;
using Sacred.UI.Maps;
using Sacred.UI.Textures;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.WorldMap;

internal sealed class Dx12WorldMapUiPass : IDisposable
{
    private readonly Dx12DeviceContext _graphics;
    private readonly Dx12TextureUploader _uploader;
    private readonly Task<WorldMapUiAssets> _load;
    private readonly Dictionary<string, int> _slots = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ID3D12Resource> _textures = [];
    private WorldMapUiAssets? _assets;
    private bool _failed;

    public Dx12WorldMapUiPass(AssetManager assets, string gameDirectory,
        Dx12DeviceContext graphics, Dx12TextureUploader uploader)
    {
        _graphics = graphics;
        _uploader = uploader;
        _load = WorldMapUiAssets.LoadAsync(gameDirectory, name => assets.LoadTextureAsync(name));
    }

    public void Build(WorldMapFrame frame)
    {
        if (frame.Ui is not { Visible: true } state || !EnsureTextures()) return;
        var layout = _assets!.Layout;
        state.Layout = layout;
        var viewport = new Vector2(_graphics.OutputWidth, _graphics.OutputHeight);
        var placement = new WorldMapUiViewport(viewport);
        var scale = placement.Scale;
        // Draw after the map labels, before minimap previews and interactive controls.
        var background = ImGuiNET.ImGui.GetBackgroundDrawList();
        if (layout.Frame is { } border)
        {
            var bounds = layout.GetMapBounds(viewport);
            background.AddRectFilled(Vector2.Zero, new(viewport.X, bounds.Position.Y), 0xFF000000);
            background.AddRectFilled(new(0, bounds.Maximum.Y), viewport, 0xFF000000);
            background.AddRectFilled(new(0, bounds.Position.Y), new(bounds.Position.X, bounds.Maximum.Y), 0xFF000000);
            background.AddRectFilled(new(bounds.Maximum.X, bounds.Position.Y), new(viewport.X, bounds.Maximum.Y), 0xFF000000);
            foreach (var quad in border.GetQuads(placement.FrameInset))
            {
                var minimum = placement.FrameToScreen(quad.Position);
                background.AddImage((nint)_slots[border.TextureName], minimum, minimum + quad.Size * scale,
                    quad.Piece.UvMinimum, quad.Piece.UvMaximum);
            }
        }
        var draw = ImGuiNET.ImGui.GetForegroundDrawList();
        DrawRegion(draw, layout.LeftPanel, layout.LeftPanelPosition, viewport);
        DrawRegion(draw, layout.RightPanel, layout.RightPanelPosition, viewport);
        foreach (var button in layout.Buttons)
        {
            var pressed = state.PressedButton == button.Button && state.HoveredButton == button.Button;
            DrawRegion(draw, pressed ? button.Down : button.Up, button.Position, viewport,
                button.Enabled ? 0xFFFFFFFFu : 0xFFAAAAAAu);
        }
    }

    private void DrawRegion(ImDrawListPtr draw, UiTextureRegion region, Vector2 position, Vector2 viewport,
        uint tint = 0xFFFFFFFF)
    {
        var minimum = WorldMapUiLayout.ToScreen(position, viewport);
        draw.AddImage((nint)_slots[region.TextureName], minimum,
            minimum + region.Size * WorldMapUiLayout.GetScale(viewport), region.UvMinimum, region.UvMaximum, tint);
    }

    private bool EnsureTextures()
    {
        if (_assets is not null) return true;
        if (_failed || !_load.IsCompleted) return false;
        try
        {
            var assets = _load.GetAwaiter().GetResult();
            if (assets.Textures.Count > Dx12DescriptorLayout.MaximumWorldMapUiTextures)
                throw new InvalidOperationException("Map UI exceeds its texture descriptor reservation.");
            foreach (var (name, texture) in assets.Textures)
            {
                var slot = Dx12DescriptorLayout.FirstWorldMapUiTexture + _textures.Count;
                var resource = _uploader.UploadRgbaTexture(_graphics.CommandList,
                    texture.Width, texture.Height, texture.Rgba8, _graphics.CurrentFrame.TransientResources);
                _textures.Add(resource);
                _uploader.CreateShaderResourceView(resource, _graphics.SrvCpuHandle(slot));
                _slots.Add(name, slot);
            }
            _assets = assets;
            EngineLog.WriteLine($"World map UI loaded: {assets.Textures.Count} original atlases; close button ready.");
            return true;
        }
        catch (Exception exception)
        {
            _failed = true;
            EngineLog.WriteLine($"World map UI loading failed: {exception.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        _ = _load.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
        foreach (var texture in _textures) texture.Dispose();
    }
}
