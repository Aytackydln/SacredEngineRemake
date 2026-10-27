using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using ImGuiNET;
using Sacred.Engine.Assets;
using Sacred.Engine.Scene;
using Sacred.Inventory.Actors;
using Sacred.Inventory.Items;
using Sacred.UI.Hud;
using Sacred.UI.Inventory;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Hud;

/// <summary>Engine adapter for the shared native item description; never draws a main-item preview.</summary>
internal sealed class Dx12ItemHoverPass : IDisposable
{
    private readonly AssetManager _resources;
    private readonly Dx12DeviceContext _graphics;
    private readonly Dx12TextureUploader _uploader;
    private readonly Task<ItemDescriptionAssets> _loading;
    private readonly Dictionary<string, int> _slots = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ID3D12Resource> _textures = new();
    private ItemDescriptionAssets? _assets;
    private ItemDescriptionContentBuilder? _content;
    private SacredItemInstance? _item;
    private string? _name;
    private IReadOnlyList<ItemDescriptionLine> _lines = [];
    private bool _failed;

    public Dx12ItemHoverPass(AssetManager resources, string gameDirectory, Dx12DeviceContext graphics, Dx12TextureUploader uploader)
    {
        _resources = resources; _graphics = graphics; _uploader = uploader;
        _loading = ItemDescriptionAssets.LoadAsync(gameDirectory, name => resources.LoadTextureAsync(name));
    }

    public IEnumerable<Sacred.Core.Pak.Weapon.SacredEquipment> SocketEquipment(SacredGameActor actor)
    {
        foreach (var slot in actor.EquipmentSlots)
        foreach (var socket in slot.Instance?.SocketRecords ?? [])
            if (socket.ItemType != 0 && _resources.TryGetEquipmentResource(socket.ItemType, out var equipment)) yield return equipment;
    }

    public void Build(SceneState scene, Dx12EquipmentPreviewPass previews, ImFontPtr font, Vector2 viewport)
    {
        var state = scene.EquipmentUi;
        if (scene.EscapeMenu is { IsOpen: true } || state.Actor is not { } actor ||
            state.ItemHover.Slot is not { } index || (uint)index >= (uint)actor.EquipmentSlots.Count ||
            actor.EquipmentSlots[index].Instance is not { } item || !EnsureAssets()) return;
        if (!ReferenceEquals(_item, item) || _name != item.ResolvedName?.Text)
        {
            _item = item; _name = item.ResolvedName?.Text; _lines = _content!.Create(item);
            EngineLog.WriteLine($"Item hover: slot {index}; {item.Definition.IdemId}; {_lines[0].Text}; {_lines.Count} lines.");
        }
        const float wrap = 380;
        var heights = new float[_lines.Count];
        var width = 200f;
        var height = 0f;
        for (var row = 0; row < _lines.Count; row++)
        {
            var iconWidth = _lines[row].IconSelector.HasValue ? 18 : 0;
            var measured = font.CalcTextSizeA(18, float.MaxValue, wrap - iconWidth, _lines[row].Text);
            width = MathF.Max(width, measured.X + iconWidth);
            heights[row] = MathF.Max(21, measured.Y);
            height += heights[row];
        }
        var sockets = ItemDescriptionSocket.Create(item);
        if (sockets.Count != 0) { height += 44; width = MathF.Max(width, sockets.Count * ItemDescriptionSocket.Pitch); }
        var bodySize = new Vector2(width + 8, height + 8);
        var nativeSize = bodySize + new Vector2(27, 21);
        var anchor = state.ItemHover.DebugSlot.HasValue ? viewport * new Vector2(0.40f, 0.18f) : state.ItemHover.Anchor;
        var (position, scale) = ItemDescriptionPopupLayout.Place(anchor, nativeSize, viewport, BottomHudLayout.GetScale(viewport));
        var origin = position + new Vector2(13, 11) * scale;
        var draw = ImGuiNET.ImGui.GetForegroundDrawList();
        draw.AddRectFilled(origin, origin + bodySize * scale, ItemDescriptionFrame.BackgroundArgb);
        foreach (var (texture, quad) in _assets!.Frame.GetQuads(bodySize.X, bodySize.Y))
            draw.AddImage(_slots[texture], origin + quad.Position * scale, origin + (quad.Position + quad.Size) * scale,
                quad.Piece.UvMinimum, quad.Piece.UvMaximum);
        var artwork = new Dx12UiArtwork(draw, _slots, origin, scale);
        var y = 4f;
        for (var row = 0; row < _lines.Count; row++)
        {
            var line = _lines[row]; var x = 4f;
            if (line.IconSelector is { } selector)
            {
                artwork.Draw(_assets.Emblems[selector], new(x, y + 1), size: new Vector2(16));
                x += 18;
            }
            draw.AddText(font, 18 * scale, origin + new Vector2(x, y) * scale,
                ItemDescriptionPopupLayout.ToAbgr(line.Color), line.Text, (wrap - (x - 4)) * scale);
            y += heights[row];
        }
        foreach (var socket in sockets)
        {
            var local = new Vector2(4 + socket.Index * ItemDescriptionSocket.Pitch, y + 4);
            var minimum = origin + local * scale;
            draw.AddRectFilled(minimum, minimum + new Vector2(32) * scale,
                ItemDescriptionPopupLayout.ToAbgr((socket.Color & 0xFFFFFF) | 0x8F000000));
            artwork.Draw(_assets.Socket, local, ItemDescriptionPopupLayout.ToAbgr((socket.Color & 0xFFFFFF) | 0x5F000000), new Vector2(32));
            if (socket.ItemType != 0 && _resources.TryGetEquipmentResource(socket.ItemType, out var equipment))
                previews.Draw(draw, equipment, minimum, scale, false);
        }
    }

    private bool EnsureAssets()
    {
        if (_assets is not null) return true;
        if (_failed || !_loading.IsCompleted) return false;
        try
        {
            var assets = _loading.GetAwaiter().GetResult();
            if (assets.Textures.Count > Dx12DescriptorLayout.MaximumItemDescriptionTextures)
                throw new InvalidOperationException("Item description atlases exceed their descriptor reservation.");
            foreach (var (name, texture) in assets.Textures)
            {
                var slot = Dx12DescriptorLayout.FirstItemDescriptionTexture + _textures.Count;
                var resource = _uploader.UploadRgbaTexture(_graphics.CommandList, texture.Width, texture.Height,
                    texture.Rgba8, _graphics.CurrentFrame.TransientResources);
                _textures.Add(resource); _slots.Add(name, slot);
                _uploader.CreateShaderResourceView(resource, _graphics.SrvCpuHandle(slot));
            }
            _assets = assets;
            _content = new(_resources.Resources, _resources.ItemSets, assets.EquipmentRules);
            EngineLog.WriteLine($"Item hover artwork loaded: {assets.Textures.Count} original atlases.");
            return true;
        }
        catch (Exception error) { _failed = true; EngineLog.WriteLine($"Item hover unavailable: {error.Message}"); return false; }
    }

    public void Dispose()
    {
        _ = _loading.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
        foreach (var texture in _textures) texture.Dispose();
    }
}
