using System;
using System.Numerics;
using System.Threading.Tasks;
using ImGuiNET;
using Sacred.Engine.Assets;
using Sacred.Engine.Scene;
using Sacred.UI.Menus;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Hud;

/// <summary>Draws the native dialog atlas and localized menu text above the world and HUD.</summary>
internal sealed class Dx12EscapeMenuPass : IDisposable
{
    private readonly Dx12DeviceContext _graphics;
    private readonly Dx12TextureUploader _uploader;
    private readonly Task<EscapeMenuAssets> _load;
    private EscapeMenuAssets? _assets;
    private ID3D12Resource? _texture;
    private bool _failed;

    public Dx12EscapeMenuPass(AssetManager assets, string gameDirectory,
        Dx12DeviceContext graphics, Dx12TextureUploader uploader)
    {
        _graphics = graphics;
        _uploader = uploader;
        _load = EscapeMenuAssets.LoadAsync(gameDirectory, name => assets.LoadTextureAsync(name));
    }

    public void Build(SceneState scene, ImFontPtr font)
    {
        if (scene.EscapeMenu is not { } menu) return;
        var ready = EnsureTexture();
        if (ready) menu.SetLayout(_assets!.Layout);
        if (!menu.IsOpen) return;
        var viewport = new Vector2(_graphics.OutputWidth, _graphics.OutputHeight);
        var scale = EscapeMenuLayout.GetScale(viewport);
        var draw = ImGuiNET.ImGui.GetForegroundDrawList();
        // Gold uses ARGB 0x6F000000. Cover the output viewport, including widescreen margins.
        draw.AddRectFilled(Vector2.Zero, viewport, ready && !_assets!.Layout.HasExport ? 0x9F000000u : 0x6F000000u);
        var origin = EscapeMenuLayout.ToScreen(Vector2.Zero, viewport);
        draw.AddRectFilled(origin, origin + EscapeMenuLayout.Size * scale, 0xB0000000);
        if (ready)
        {
            foreach (var quad in _assets!.Frame.GetQuads())
            {
                var minimum = EscapeMenuLayout.ToScreen(quad.Position, viewport);
                draw.AddImage((nint)Dx12DescriptorLayout.EscapeMenuTexture, minimum,
                    minimum + quad.Size * scale, quad.Piece.UvMinimum, quad.Piece.UvMaximum);
            }
        }
        var fontSize = EscapeMenuLayout.FontSize * scale;
        if (menu.ConfirmingQuit)
        {
            // Wrap at spaces to keep localized confirmation text inside the dialog.
            var position = EscapeMenuLayout.ToScreen(new(20, 24), viewport);
            draw.AddText(font, fontSize, position, 0xFFE7DCD1, menu.QuitPrompt, 224 * scale);
        }
        for (var i = 0; i < menu.Entries.Count; i++)
        {
            var entry = menu.Entries[i];
            var bounds = EscapeMenuLayout.GetRowBounds(i, menu.ConfirmingQuit, viewport);
            var textSize = font.CalcTextSizeA(fontSize, float.MaxValue, 0, entry.Caption);
            var selected = menu.SelectedIndex == i && entry.Enabled;
            var color = !entry.Enabled ? 0xFF777777u : selected ? 0xFF80FFFFu : 0xFFFFFFFFu;
            if (menu.PressedIndex == i) color = 0xFF50BBDDu;
            draw.AddText(font, fontSize, bounds.Position + (bounds.Size - textSize) / 2, color, entry.Caption);
        }
    }

    private bool EnsureTexture()
    {
        if (_assets is not null) return true;
        if (_failed || !_load.IsCompleted) return false;
        try
        {
            var assets = _load.GetAwaiter().GetResult();
            var texture = assets.Texture;
            _texture = _uploader.UploadRgbaTexture(_graphics.CommandList, texture.Width, texture.Height,
                texture.Rgba8, _graphics.CurrentFrame.TransientResources);
            _uploader.CreateShaderResourceView(_texture, _graphics.SrvCpuHandle(Dx12DescriptorLayout.EscapeMenuTexture));
            _assets = assets;
            EngineLog.WriteLine($"Escape menu loaded: native dialog atlas {assets.Frame.TextureName}, {(assets.Layout.HasExport ? 5 : 4)} localized entries.");
            return true;
        }
        catch (Exception exception)
        {
            _failed = true;
            EngineLog.WriteLine($"Escape menu loading failed: {exception.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        _ = _load.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
        _texture?.Dispose();
    }
}
