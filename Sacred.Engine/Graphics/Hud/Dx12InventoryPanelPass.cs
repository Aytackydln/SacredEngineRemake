using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Sacred.Engine.Assets;
using Sacred.UI.Hud;
using Sacred.UI.Inventory;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Hud;

/// <summary>Hosts a vertical composition of independent player-stats and equipment-slot components.</summary>
internal sealed class Dx12InventoryPanelPass : IDisposable
{
    private readonly Dx12DeviceContext _graphics;
    private readonly Dx12TextureUploader _uploader;
    private readonly Task<InventoryPanelAssets> _loading;
    private readonly Dictionary<string, int> _slots = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ID3D12Resource> _textures = [];
    private InventoryPanelAssets? _assets;
    private bool _failed;

    public Dx12InventoryPanelPass(AssetManager assets, string gameDirectory,
        Dx12DeviceContext graphics, Dx12TextureUploader uploader)
    {
        _graphics = graphics; _uploader = uploader;
        _loading = InventoryPanelAssets.LoadAsync(gameDirectory, name => assets.LoadTextureAsync(name));
    }

    public void Build(EquipmentUiState state, Dx12EquipmentPreviewPass previews, Vector2 viewport)
    {
        if (state.Actor is not { } actor || !EnsureAssets()) return;
        state.Layout ??= new(actor, _assets!.Equipment);
        if (!state.IsOpen) return;
        var origin = InventoryPanelLayout.Origin(viewport);
        var scale = BottomHudLayout.GetScale(viewport);
        var draw = ImGuiNET.ImGui.GetBackgroundDrawList();
        Dx12PlayerStatsPanel.Build(_assets!.Stats.Layout, draw, _slots,
            origin + InventoryPanelLayout.StatsOrigin * scale, scale);
        Dx12EquipmentSlotsPanel.Build(_assets.Equipment, state.Layout.Equipment, actor, state.SelectedSlot,
            previews, draw, _slots, origin + InventoryPanelLayout.EquipmentOrigin * scale, scale);
    }

    private bool EnsureAssets()
    {
        if (_assets is not null) return true;
        if (_failed || !_loading.IsCompleted) return false;
        try
        {
            var assets = _loading.GetAwaiter().GetResult();
            if (assets.Textures.Count > Dx12DescriptorLayout.MaximumEquipmentAtlases)
                throw new InvalidOperationException("Equipment artwork exceeds its atlas reservation.");
            foreach (var (name, texture) in assets.Textures)
            {
                var slot = Dx12DescriptorLayout.FirstEquipmentArt + _textures.Count;
                var resource = _uploader.UploadRgbaTexture(_graphics.CommandList, texture.Width, texture.Height,
                    texture.Rgba8, _graphics.CurrentFrame.TransientResources);
                _textures.Add(resource); _slots.Add(name, slot);
                _uploader.CreateShaderResourceView(resource, _graphics.SrvCpuHandle(slot));
            }
            _assets = assets;
            EngineLog.WriteLine($"Inventory artwork loaded: {assets.Textures.Count} original atlases; stats and class slots ready; hands use the taskbar.");
            return true;
        }
        catch (Exception error)
        {
            _failed = true; EngineLog.WriteLine($"Inventory artwork unavailable: {error.Message}"); return false;
        }
    }

    public void Dispose()
    {
        _ = _loading.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
        foreach (var texture in _textures) texture.Dispose();
    }
}
