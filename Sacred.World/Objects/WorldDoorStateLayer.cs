using System.Numerics;

namespace Sacred.World.Objects;

/// <summary>Session door state independent of model loading and streamed sector lifetime.</summary>
public sealed class WorldDoorStateLayer
{
    private readonly Dictionary<uint, WorldDoorDefinition> _doors = [];
    private readonly HashSet<uint> _open = [];
    private readonly Dictionary<(int X, int Y, int Surface), HashSet<uint>> _cells = [];
    public int Revision { get; private set; }

    public WorldDoorStateLayer(IEnumerable<WorldDoorDefinition> definitions)
    {
        foreach (var door in definitions)
        {
            if (!_doors.TryAdd(door.Id, door)) continue;
            if (door.InitiallyOpen) _open.Add(door.Id);
            foreach (var cell in door.BlockingCells)
            {
                // Exterior and the first indoor floor share the movement plane.
                var key = (cell.X, cell.Y, Math.Max(1, cell.Z));
                if (!_cells.TryGetValue(key, out var owners)) _cells[key] = owners = [];
                owners.Add(door.Id);
            }
        }
    }

    public bool IsOpen(uint id) => _open.Contains(id);
    public bool IsLocked(uint id) => !_doors.TryGetValue(id, out var door) || door.Locked;
    public bool WouldOverlap(uint id, Vector2 position, float radius, int surfaceLevel)
    {
        if (!_doors.TryGetValue(id, out var door)) return false;
        foreach (var cell in door.BlockingCells)
        {
            if (Math.Max(1, cell.Z) != Math.Max(1, surfaceLevel)) continue;
            var nearest = Vector2.Clamp(position,
                new(cell.X, cell.Y), new(cell.X + 1, cell.Y + 1));
            if (Vector2.DistanceSquared(nearest, position) < radius * radius) return true;
        }
        return false;
    }
    public bool IsBlocked(int x, int y, int surfaceLevel) =>
        _cells.TryGetValue((x, y, Math.Max(1, surfaceLevel)), out var doors) &&
        doors.Any(id => !_open.Contains(id));

    public bool TrySetOpen(uint id, bool open)
    {
        if (IsLocked(id)) return false;
        var changed = open ? _open.Add(id) : _open.Remove(id);
        if (changed) Revision++;
        return true;
    }
}
