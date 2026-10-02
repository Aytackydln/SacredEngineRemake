using System;
using System.Collections.Generic;
using Sacred.Core.World.Sector;
using Sacred.Engine.Rendering;

namespace Sacred.Engine.Graphics.Terrain;

/// <summary>Pins requested terrain, then uses a bounded budget for ahead and recently visited sectors.</summary>
internal sealed class SectorTextureRetention
{
    private const int MaximumResidentSectors = 12;
    private const long MaximumTexelBytes = 2L * 1024 * 1024 * 1024;
    private readonly List<TerrainSectorComposition> _selected = new(MaximumResidentSectors);
    private readonly HashSet<SectorCoord> _coords = [];
    private readonly List<SectorTexture> _warm = new(MaximumResidentSectors);
    private long _sequence;

    public IReadOnlyList<TerrainSectorComposition> Select(IReadOnlyList<TerrainSectorComposition> visible,
        IReadOnlyList<TerrainSectorComposition> ahead, IReadOnlyDictionary<SectorCoord, SectorTexture> resident,
        SectorCoord center, int descriptorCapacity)
    {
        _selected.Clear();
        _coords.Clear();
        _warm.Clear();
        var capacity = Math.Min(MaximumResidentSectors, descriptorCapacity);
        long bytes = 0;
        _sequence++;
        foreach (var image in visible)
        {
            _selected.Add(image);
            _coords.Add(image.Coord);
            bytes += TexelBytes(image);
            if (resident.TryGetValue(image.Coord, out var texture)) texture.LastUsedSequence = _sequence;
        }
        foreach (var image in ahead)
        {
            if (_coords.Contains(image.Coord) || _selected.Count >= capacity || bytes + TexelBytes(image) > MaximumTexelBytes) continue;
            _selected.Add(image);
            _coords.Add(image.Coord);
            bytes += TexelBytes(image);
        }
        foreach (var texture in resident.Values)
            if (!_coords.Contains(texture.Composition.Coord) && Math.Abs(texture.Composition.Coord.X - center.X) <= 2 &&
                Math.Abs(texture.Composition.Coord.Y - center.Y) <= 2) _warm.Add(texture);
        _warm.Sort(static (left, right) => right.LastUsedSequence.CompareTo(left.LastUsedSequence));
        foreach (var texture in _warm)
        {
            var image = texture.Composition;
            if (_selected.Count >= capacity || bytes + TexelBytes(image) > MaximumTexelBytes) continue;
            _selected.Add(image);
            bytes += TexelBytes(image);
        }
        return _selected;
    }
    private static long TexelBytes(TerrainSectorComposition image) => (long)image.Width * image.Height * 4 * Dx12SectorTextureCache.TexturesPerSector;
}
