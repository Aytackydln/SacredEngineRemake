namespace Sacred.Core.World.Sector;

public sealed class IndoorTileGroupLayer
{
    private readonly object _sync = new();
    private IndoorTileGroup[] _groups = [];

    public IReadOnlyList<IndoorTileGroup> Groups => Volatile.Read(ref _groups);
    public int Count => Volatile.Read(ref _groups).Length;

    public void Add(IndoorTileGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        lock (_sync)
        {
            foreach (var existing in _groups)
                if (existing.Id == group.Id)
                    return;

            var updated = new IndoorTileGroup[_groups.Length + 1];
            _groups.CopyTo(updated, 0);
            updated[^1] = group;
            Volatile.Write(ref _groups, updated);
        }
    }
}
