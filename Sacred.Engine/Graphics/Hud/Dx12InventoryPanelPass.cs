using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using ImGuiNET;
using Sacred.Core.GameRes;
using Sacred.Engine.Assets;
using Sacred.UI.CombatArts;
using Sacred.UI.Hud;
using Sacred.UI.Inventory;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Hud;

/// <summary>Hosts the right-hand character column and independent lower-left inventory pages.</summary>
internal sealed class Dx12InventoryPanelPass : IDisposable
{
    private readonly Dx12DeviceContext _graphics;
    private readonly Dx12TextureUploader _uploader;
    private readonly Task<InventoryPanelAssets> _loading;
    private readonly Dictionary<string, int> _slots = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ID3D12Resource> _textures = [];
    private InventoryPanelAssets? _assets;
    private bool _failed;
    private readonly GameResStore _resources;
    private readonly bool _gold;

    public Dx12InventoryPanelPass(AssetManager assets, string gameDirectory,
        Dx12DeviceContext graphics, Dx12TextureUploader uploader)
    {
        _graphics = graphics; _uploader = uploader;
        _resources = assets.Resources;
        _gold = assets.CreatureTemplates?.Entries.FirstOrDefault()?.RecordSize == 86;
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
        var tabsOrigin = InventoryTabsLayout.Origin(viewport);
        DrawTabs(state, draw, tabsOrigin, scale);
        var contentOrigin = tabsOrigin + InventoryTabsLayout.ContentOrigin * scale;
        if (state.ActiveTab == InventoryTab.CombatArts)
            Dx12CombatArtPanel.Build(state.CombatArts, actor, _assets!, _resources, _gold, draw, _slots, contentOrigin, scale);
        else DrawBackpack(draw, contentOrigin, scale);
    }

    private void DrawBackpack(ImDrawListPtr draw, Vector2 origin, float scale)
    {
        var size = new Vector2(InventoryTabsLayout.Width, InventoryTabsLayout.ContentHeight);
        var artwork = new Dx12UiArtwork(draw, _slots, origin, scale);
        artwork.Draw(_assets!.Equipment.Background, Vector2.Zero, 0xEFFFFFFF, size);
        draw.AddRectFilled(origin, origin + size * scale, 0xB0181613);
        for (var x = 0; x <= 640; x += 40)
            draw.AddLine(origin + new Vector2(x, 0) * scale, origin + new Vector2(x, 256) * scale, 0x806F6553);
        for (var y = 0; y <= 256; y += 32)
            draw.AddLine(origin + new Vector2(0, y) * scale, origin + new Vector2(640, y) * scale, 0x806F6553);
        draw.AddRect(origin, origin + size * scale, 0xFF837259, 0, ImDrawFlags.None, 2 * scale);
    }

    public void DrawQuickSlotArts(CombatArtPanelState state, BottomHudQuickSlotLayout layout, Vector2 viewport)
    {
        if (!EnsureAssets()) return;
        var draw = ImGuiNET.ImGui.GetBackgroundDrawList();
        for (var index = 0; index < layout.CombatArtSlots.Count; index++)
        {
            if (state.QuickSlots[index] is not { } code || !state.CanAssign(code)) continue;
            var bounds = BottomHudQuickSlotLayout.GetBounds(layout.CombatArtSlots[index], viewport);
            var inset = new Vector2(6) * BottomHudLayout.GetScale(viewport);
            Dx12CombatArtPanel.DrawIcon(code, _gold, draw, _slots, bounds.Position + inset, bounds.Size - inset * 2);
        }
    }

    private static void DrawTabs(EquipmentUiState state, ImDrawListPtr draw, Vector2 origin, float scale)
    {
        foreach (var tab in new[] { InventoryTab.Inventory, InventoryTab.CombatArts })
        {
            var bounds = InventoryTabsLayout.TabBounds(tab);
            var minimum = origin + bounds.Position * scale;
            var maximum = minimum + bounds.Size * scale;
            var selected = state.ActiveTab == tab;
            draw.AddRectFilled(minimum, maximum, selected ? 0xF04A4030 : 0xE022211D, 3 * scale);
            draw.AddRect(minimum, maximum, selected ? 0xFF32B9DA : 0xFF837259, 3 * scale);
            var label = tab == InventoryTab.Inventory ? "Inventory" : "Combat Arts";
            var fontSize = 14 * scale;
            var textSize = ImGuiNET.ImGui.CalcTextSize(label) * (fontSize / ImGuiNET.ImGui.GetFontSize());
            draw.AddText(ImGuiNET.ImGui.GetFont(), fontSize, (minimum + maximum - textSize) / 2, 0xFFE6DFCE, label);
        }
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
            EngineLog.WriteLine($"Inventory artwork loaded: {assets.Textures.Count} original atlases; Inventory and character Combat Arts tabs ready.");
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
