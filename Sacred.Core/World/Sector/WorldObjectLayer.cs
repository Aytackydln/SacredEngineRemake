namespace Sacred.Core.World.Sector;

/// <summary>
/// Dynamic world-object records referenced by a WLDX tile's native <c>wobj</c>
/// chain. These are separate from the static scenery chain (<c>sobj</c>).
/// </summary>
public sealed class WorldObjectLayer
{
    private readonly List<StaticWorldObject> _objects = [];

    public IReadOnlyList<StaticWorldObject> Objects => _objects;
    public int Count => _objects.Count;

    public void Add(StaticWorldObject worldObject) => _objects.Add(worldObject);
}
