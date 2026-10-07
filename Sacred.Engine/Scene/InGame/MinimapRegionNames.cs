using System.Collections.Generic;
using System.Numerics;
using Sacred.World;
using Sacred.World.Map;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Associates localized startup map names with their authored sector regions.</summary>
internal sealed class MinimapRegionNames
{
    private readonly SacredWorldArchive _world;
    private readonly Dictionary<byte, List<WorldMapLabel>> _names = new();

    public MinimapRegionNames(SacredWorldArchive world, WorldMapAnnotations annotations)
    {
        _world = world;
        foreach (var label in annotations.Labels)
        {
            if (world.GetRegion(label.WorldPosition.X, label.WorldPosition.Y) is not { } region)
                continue;
            if (!_names.TryGetValue(region, out var names))
                _names.Add(region, names = new());
            names.Add(label);
        }
    }

    public string GetDisplayName(Vector2 position)
    {
        if (_world.GetRegion(position.X, position.Y) is not { } region ||
            !_names.TryGetValue(region, out var names))
            return string.Empty;

        // A region can contain multiple authored place names. Use its nearest anchor.
        var closest = names[0];
        var distance = Vector2.DistanceSquared(position, closest.WorldPosition);
        foreach (var name in names)
        {
            var candidateDistance = Vector2.DistanceSquared(position, name.WorldPosition);
            if (candidateDistance >= distance)
                continue;
            closest = name;
            distance = candidateDistance;
        }
        return closest.Text;
    }
}
