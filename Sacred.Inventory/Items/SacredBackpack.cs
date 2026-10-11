using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Sacred.Core.Runtime;

namespace Sacred.Inventory.Items;

/// <summary>Native rectangular cell storage. Every occupied cell resolves to one
/// anchor; dimensions come from Weapon.pak and items are never rotated implicitly.</summary>
public sealed class SacredBackpack
{
    private readonly Dictionary<Guid, SacredBackpackPlacement> _placements = new();
    private readonly Guid?[] _cells;

    internal SacredBackpack(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > byte.MaxValue || height > byte.MaxValue ||
            (long)width * height > SacredInventoryEntryLayout.CellCapacity)
            throw new ArgumentOutOfRangeException(nameof(width), "The native inventory has at most 256 cells.");
        Width = width;
        Height = height;
        _cells = new Guid?[width * height];
        Placements = new ReadOnlyDictionary<Guid, SacredBackpackPlacement>(_placements);
    }

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyDictionary<Guid, SacredBackpackPlacement> Placements { get; }
    public int UsedCells => _placements.Values.Sum(placement => placement.Width * placement.Height);

    public SacredBackpackPlacement? GetAt(int x, int y)
    {
        if ((uint)x >= Width || (uint)y >= Height) throw new ArgumentOutOfRangeException(nameof(x));
        return _cells[y * Width + x] is { } id ? _placements[id] : null;
    }

    internal bool TryPlace(SacredItemInstance item, int x, int y)
    {
        var width = item.Definition.Width;
        var height = item.Definition.Height;
        if (width == 0 || height == 0 || x < 0 || y < 0 || x > Width - width || y > Height - height)
            return false;
        for (var row = y; row < y + height; row++)
        for (var column = x; column < x + width; column++)
            if (_cells[row * Width + column] is { } occupant && occupant != item.InstanceId) return false;

        Remove(item.InstanceId);
        _placements.Add(item.InstanceId, new(item, x, y, width, height));
        for (var row = y; row < y + height; row++)
        for (var column = x; column < x + width; column++) _cells[row * Width + column] = item.InstanceId;
        return true;
    }

    internal bool TryPlaceFirst(SacredItemInstance item, bool rowFirst)
    {
        // Native getFreeslot supports both orders; the default is column first.
        if (rowFirst)
        {
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++) if (TryPlace(item, x, y)) return true;
        }
        else
        {
            for (var x = 0; x < Width; x++)
            for (var y = 0; y < Height; y++) if (TryPlace(item, x, y)) return true;
        }
        return false;
    }

    internal bool Remove(Guid id)
    {
        if (!_placements.Remove(id, out var placement)) return false;
        for (var y = placement.Y; y < placement.Y + placement.Height; y++)
        for (var x = placement.X; x < placement.X + placement.Width; x++) _cells[y * Width + x] = null;
        return true;
    }

    internal void CopyTo(SacredBackpack target)
    {
        foreach (var placement in _placements.Values)
            if (!target.TryPlace(placement.Item, placement.X, placement.Y))
                throw new InvalidOperationException("The cloned backpack cannot retain its placement.");
    }
}
