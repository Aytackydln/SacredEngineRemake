using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using ImGuiNET;
using Sacred.Engine.Assets;
using Sacred.Engine.Scene;
using Sacred.UI.Hud;
using Sacred.UI.Textures;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Hud;

/// <summary>Uploads original HUD atlases and draws Sacred.UI's components at output resolution.</summary>
internal sealed class Dx12BottomHudPass : IDisposable
{
    private readonly Dx12DeviceContext _graphics;
    private readonly Dx12TextureUploader _uploader;
    private readonly Task<BottomHudAssets> _load;
    private readonly Dictionary<string, int> _slots = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ID3D12Resource> _textures = [];
    private BottomHudAssets? _assets;
    private bool _failed;

    public Dx12BottomHudPass(AssetManager assets, string gameDirectory,
        Dx12DeviceContext graphics, Dx12TextureUploader uploader)
    {
        _graphics = graphics;
        _uploader = uploader;
        _load = BottomHudAssets.LoadAsync(gameDirectory, name => assets.LoadTextureAsync(name));
    }

    public void Build(SceneState scene)
    {
        if (scene.Hud is not { Visible: true } state || !EnsureTextures()) return;
        var assets = _assets!;
        var layout = assets.Layout;
        state.Layout = layout;
        var viewport = new Vector2(_graphics.OutputWidth, _graphics.OutputHeight);
        // Background draw data is recorded with the output UI, behind any diagnostic windows.
        var draw = ImGuiNET.ImGui.GetBackgroundDrawList();
        DrawRegion(draw, layout.Panel, layout.PanelPosition, viewport);
        DrawDial(draw, BottomHudAssets.DayNightDisc, state.DayFraction, viewport);
        DrawDial(draw, BottomHudAssets.DayNightMask, 0, viewport);
        foreach (var decoration in layout.Decorations)
            DrawRegion(draw, decoration.Region, decoration.Position, viewport);
        foreach (var button in layout.Buttons)
        {
            var down = state.PressedButton == button.Button && state.HoveredButton == button.Button;
            DrawRegion(draw, down ? button.Down : button.Up, button.Position, viewport);
        }
    }

    private bool EnsureTextures()
    {
        if (_assets is not null) return true;
        if (_failed || !_load.IsCompleted) return false;
        try
        {
            var assets = _load.GetAwaiter().GetResult();
            if (assets.Textures.Count > Dx12DescriptorLayout.MaximumHudTextures)
                throw new InvalidOperationException("The HUD atlas count exceeds its descriptor reservation.");
            foreach (var (name, texture) in assets.Textures)
            {
                var slot = Dx12DescriptorLayout.FirstHudTexture + _textures.Count;
                var resource = _uploader.UploadRgbaTexture(_graphics.CommandList,
                    texture.Width, texture.Height, texture.Rgba8, _graphics.CurrentFrame.TransientResources);
                _textures.Add(resource);
                _uploader.CreateShaderResourceView(resource, _graphics.SrvCpuHandle(slot));
                _slots.Add(name, slot);
            }
            _assets = assets;
            EngineLog.WriteLine($"Bottom HUD loaded: native executable definitions, {assets.Textures.Count} original atlases; map button ready.");
            return true;
        }
        catch (Exception exception)
        {
            _failed = true;
            EngineLog.WriteLine($"Bottom HUD loading failed: {exception.Message}");
            return false;
        }
    }

    private void DrawRegion(ImDrawListPtr draw, UiTextureRegion region, Vector2 position, Vector2 viewport)
    {
        var minimum = BottomHudLayout.ToScreen(position, viewport);
        draw.AddImage((nint)_slots[region.TextureName], minimum,
            minimum + region.Size * BottomHudLayout.GetScale(viewport), region.UvMinimum, region.UvMaximum);
    }

    private void DrawDial(ImDrawListPtr draw, string name, float fraction, Vector2 viewport)
    {
        var r = BottomHudLayout.DialRadius;
        Vector2 Corner(float x, float y) => BottomHudLayout.ToScreen(DayNightDial.Transform(new(x, y), fraction), viewport);
        draw.AddImageQuad((nint)_slots[name], Corner(-r, -r), Corner(r, -r), Corner(r, r), Corner(-r, r),
            Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY);
    }

    public void Dispose()
    {
        // Observe faults even if this world was disposed before the asynchronous load completed.
        _ = _load.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
        foreach (var texture in _textures) texture.Dispose();
    }
}
