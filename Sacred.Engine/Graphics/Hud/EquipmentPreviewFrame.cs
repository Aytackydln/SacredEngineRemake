using System;
using System.Collections.Generic;
using System.Numerics;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Hud;

/// <summary>Slot resources reused only after their owning engine frame's fence retires.</summary>
internal sealed class EquipmentPreviewFrame : IDisposable
{
    public int Revision { get; set; } = -1;
    public ID3D12Resource? Target { get; set; }
    public ID3D12Resource? Depth { get; set; }
    public List<ID3D12Resource> Resources { get; } = [];
    public List<EquipmentPreviewDraw> Draws { get; } = [];
    public Dictionary<string, (int Slot, bool Alpha)> Textures { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<uint, (Vector2 Position, EquipmentPreviewAsset Asset)> Images { get; } = [];
    public Vector2 Size { get; set; }

    public void Dispose()
    {
        Target?.Dispose(); Target = null;
        Depth?.Dispose(); Depth = null;
        foreach (var resource in Resources) resource.Dispose();
        Resources.Clear(); Draws.Clear(); Textures.Clear(); Images.Clear();
    }
}

internal sealed record EquipmentPreviewDraw(EquipmentPreviewAsset Asset, Vector2 Position,
    VertexBufferView Vertices, IndexBufferView Indices);
