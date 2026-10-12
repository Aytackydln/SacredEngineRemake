using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Texture;
using Sacred.Engine.Assets;
using Sacred.Engine.Scene;
using Sacred.Inventory.Actors;
using Sacred.UI.Character;
using Sacred.UI.Hud;
using Sacred.UI.Textures;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Hud;

/// <summary>Player and party artwork bound directly to runtime character components.</summary>
internal sealed class Dx12ActorPortraitPass : IDisposable
{
    private readonly AssetManager _assets;
    private readonly Dx12DeviceContext _graphics;
    private readonly Dx12TextureUploader _uploader;
    private readonly Task<ActorPortraitAssets> _loading;
    private readonly Dictionary<string, Task<TextureAsset>> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _slots = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ID3D12Resource> _textures = [];
    private readonly HashSet<string> _failedTextures = new(StringComparer.OrdinalIgnoreCase);
    private ActorPortraitAssets? _art;
    private bool _failed;

    public Dx12ActorPortraitPass(AssetManager assets, string gameDirectory,
        Dx12DeviceContext graphics, Dx12TextureUploader uploader)
    {
        _assets = assets; _graphics = graphics; _uploader = uploader;
        _loading = ActorPortraitAssets.LoadAsync(gameDirectory);
    }

    public void Build(SceneState scene)
    {
        if (scene.Hud is not { Visible: true } || scene.Portraits.Player is not { } player || !EnsureDefinitions()) return;
        var state = scene.Portraits;
        state.Layout ??= new(_art!);
        var viewport = new Vector2(_graphics.OutputWidth, _graphics.OutputHeight);
        var scale = BottomHudLayout.GetScale(viewport);
        var draw = ImGuiNET.ImGui.GetBackgroundDrawList();
        Draw(player, 0, true);
        for (var index = 0; index < Math.Min(ActorPortraitLayout.MaximumCompanions, state.Party.Members.Count); index++)
            Draw(state.Party.Members[index], index + 1, false);

        void Draw(SacredGameActor actor, int index, bool isPlayer)
        {
            var origin = ActorPortraitLayout.Origin(viewport) + state.Layout.Position(index) * scale;
            var health = actor.Character.Health.Fraction;
            var ringSize = _art!.HealthFull.Size * scale;
            var split = origin.Y + MathF.Truncate(_art.HealthFull.Size.Y * (1 - health)) * scale;
            // Native renderMercenary splits the complete empty/full controls vertically.
            if (health < 1)
            {
                draw.PushClipRect(origin, new(origin.X + ringSize.X, split), true);
                Region(_art.HealthEmpty, origin, ringSize); draw.PopClipRect();
            }
            if (health > 0)
            {
                draw.PushClipRect(new(origin.X, split), origin + ringSize, true);
                Region(_art.HealthFull, origin, ringSize); draw.PopClipRect();
            }
            var type = actor.Progression.Template?.ItemType;
            if (type is { } actorType && _art.GetPortrait(actorType) is { } portrait)
                Region(portrait, origin + ActorPortraitLayout.PortraitOffset * scale, ActorPortraitLayout.PortraitSize * scale);
            if (isPlayer)
                Region(_art.ExperienceFrame, origin + ActorPortraitLayout.ExperienceOffset * scale,
                    _art.ExperienceFrame.Size * scale);
        }

        void Region(UiTextureRegion region, Vector2 minimum, Vector2 size)
        {
            if (!EnsureTexture(region.TextureName, out var slot)) return;
            draw.AddImage(slot, minimum, minimum + size, region.UvMinimum, region.UvMaximum);
        }
    }

    private bool EnsureDefinitions()
    {
        if (_art is not null) return true;
        if (_failed || !_loading.IsCompleted) return false;
        try
        {
            _art = _loading.GetAwaiter().GetResult();
            EngineLog.WriteLine($"Player/party portraits loaded: {_art.Selectors.Count} native actor selectors; health reads cCreatureHero TP[2]/TP[1].");
            return true;
        }
        catch (Exception error) { _failed = true; EngineLog.WriteLine($"Actor portrait definitions failed: {error.Message}"); return false; }
    }

    private bool EnsureTexture(string name, out int slot)
    {
        if (_slots.TryGetValue(name, out slot)) return true;
        if (_failedTextures.Contains(name)) return false;
        if (!_pending.TryGetValue(name, out var task))
        { task = Task.Run(() => _assets.LoadTextureAsync(name)); _pending.Add(name, task); }
        if (!task.IsCompleted) return false;
        try
        {
            var texture = task.GetAwaiter().GetResult();
            if (_textures.Count >= Dx12DescriptorLayout.MaximumPortraitTextures)
                throw new InvalidOperationException("Portrait atlas reservation exhausted.");
            slot = Dx12DescriptorLayout.FirstPortraitTexture + _textures.Count;
            var resource = _uploader.UploadRgbaTexture(_graphics.CommandList, texture.Width, texture.Height,
                texture.Rgba8, _graphics.CurrentFrame.TransientResources);
            _textures.Add(resource); _slots.Add(name, slot);
            _uploader.CreateShaderResourceView(resource, _graphics.SrvCpuHandle(slot));
            _pending.Remove(name);
            EngineLog.WriteLine($"Portrait atlas loaded: {name}.");
            return true;
        }
        catch (Exception error)
        { _failedTextures.Add(name); _pending.Remove(name); EngineLog.WriteLine($"Portrait atlas failed: {name}; {error.Message}"); return false; }
    }

    public void Dispose()
    {
        _ = _loading.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        foreach (var task in _pending.Values) _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        foreach (var texture in _textures) texture.Dispose();
    }
}
