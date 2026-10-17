using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;
using Sacred.Assets.Executable;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.Pak.Creature;
using Sacred.Engine.Assets;
using Sacred.Engine.Graphics.ImGui;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;
using Sacred.World.Geometry;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Hud;

/// <summary>Head-anchored native billboards in the output UI pass: no world lighting or unlit HDR boost.</summary>
internal sealed class Dx12NpcOverheadPass : IDisposable
{
    private readonly AssetManager _assets;
    private readonly Dx12DeviceContext _graphics;
    private readonly Dx12TextureUploader _uploader;
    private readonly Task<SacredNpcIconDefinitions> _loading;
    private readonly Dictionary<string, Task<TextureAsset>> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _slots = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ID3D12Resource> _textures = [];
    private SacredNpcIconDefinitions? _definitions;
    private bool _definitionFailure;
    private readonly long _started = Stopwatch.GetTimestamp();

    public Dx12NpcOverheadPass(AssetManager assets, string gameDirectory, Dx12DeviceContext graphics, Dx12TextureUploader uploader)
    {
        _assets = assets; _graphics = graphics; _uploader = uploader;
        _loading = Task.Run(() => SacredNpcIconDefinitions.Load(gameDirectory));
    }

    public void Build(SceneState scene)
    {
        var state = scene.NpcOverheads;
        state.DrawnIcons = 0;
        state.DrawnGoldenIcons = 0;
        state.DrawnGlowLayers = 0;
        state.AnimationMilliseconds = state.AnimationMillisecondsOverride ?? unchecked((uint)(long)Stopwatch.GetElapsedTime(_started).TotalMilliseconds);
        if (_definitions is null && !_definitionFailure && _loading.IsCompleted)
        {
            try
            {
                _definitions = _loading.GetAwaiter().GetResult();
                state.ArtworkStatus = "native definitions ready";
                EngineLog.WriteLine($"NPC overhead definitions loaded: {_definitions.SimulationIcons.Count} simulation states; original dialog/service and ALT textures.");
            }
            catch (Exception error)
            {
                _definitionFailure = true; state.ArtworkStatus = error.Message;
                EngineLog.WriteLine($"NPC overhead definitions failed: {error.Message}");
            }
        }
        if (_definitions is null || state.Camera is not { } camera || scene.EscapeMenu is { IsOpen: true }) return;
        var draw = ImGuiNET.ImGui.GetBackgroundDrawList();
        var unit = WorldModelPose.Scale * camera.GetViewportZoom(_graphics.OutputHeight);
        draw.PushClipRect(Vector2.Zero, new(_graphics.OutputWidth, _graphics.OutputHeight), true);
        foreach (var actor in state.Actors)
        {
            var selector = actor.State.Select(state.ShowStates, actor.TemplateDialog, actor.Animal,
                _definitions.SimulationIcons, state.DialogIcon);
            if (selector is not { } icon || _definitions.Texture(icon) is not { } texture ||
                !NpcOverheadAnchor.TryProject(actor.Model, camera, _graphics.OutputWidth, _graphics.OutputHeight, out var head) ||
                !TryTexture(texture, out var slot)) continue;
            // Gold 0x40989C..0x4099BB: camera-facing quad X=-10..10, Y=20..40 above the head.
            var argb = _definitions.ColorArgb(icon);
            var abgr = (argb & 0xFF00FF00) | ((argb & 0xFF) << 16) | ((argb >> 16) & 0xFF);
            draw.AddImage((nint)slot, head + new Vector2(-10, -40) * unit, head + new Vector2(10, -20) * unit,
                Vector2.Zero, Vector2.One, abgr);
            var glow = SacredNpcOverheadAnimation.Glow(state.AnimationMilliseconds, actor.Reference);
            // Gold's second pass ignores texture alpha and adds texture RGB * pulse.
            // Keep vertex alpha opaque so ImGui emits the quad; the shader emits zero alpha.
            var glowColor = 0xFF000000u | (uint)(glow * 0x010101);
            draw.AddImage(ImGuiTextureReference.Additive(slot), head + new Vector2(-10, -40) * unit,
                head + new Vector2(10, -20) * unit, Vector2.Zero, Vector2.One, glowColor);
            state.DrawnIcons++;
            state.DrawnGlowLayers++;
            if (argb == 0xFFFFFF00) state.DrawnGoldenIcons++;
        }
        draw.PopClipRect();
    }

    private bool TryTexture(string name, out int slot)
    {
        if (_slots.TryGetValue(name, out slot)) return true;
        if (_failed.Contains(name)) return false;
        if (!_pending.TryGetValue(name, out var loading)) _pending[name] = loading = _assets.LoadTextureAsync(name);
        if (!loading.IsCompleted) return false;
        try
        {
            var texture = loading.GetAwaiter().GetResult();
            if (_textures.Count == Dx12DescriptorLayout.MaximumNpcOverheadTextures) throw new InvalidOperationException("NPC overhead texture reservation exhausted.");
            slot = Dx12DescriptorLayout.FirstNpcOverheadTexture + _textures.Count;
            var resource = _uploader.UploadRgbaTexture(_graphics.CommandList, texture.Width, texture.Height,
                texture.Rgba8, _graphics.CurrentFrame.TransientResources);
            _uploader.CreateShaderResourceView(resource, _graphics.SrvCpuHandle(slot));
            _textures.Add(resource); _slots.Add(name, slot); _pending.Remove(name);
            EngineLog.WriteLine($"NPC overhead texture loaded: {name} ({texture.Width}x{texture.Height}).");
            return true;
        }
        catch (Exception error)
        {
            _failed.Add(name); _pending.Remove(name);
            EngineLog.WriteLine($"NPC overhead texture failed: {name}: {error.Message}");
            return false;
        }
    }
    public void Dispose()
    {
        _ = _loading.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
        foreach (var task in _pending.Values) _ = task.ContinueWith(done => _ = done.Exception, TaskContinuationOptions.OnlyOnFaulted);
        foreach (var texture in _textures) texture.Dispose();
    }
}
