namespace Sacred.Core.World.Sector;

public sealed class StaticObjectLayer
{
    private readonly List<StaticWorldObject> _objects = [];

    public int Count => _objects.Count;
    public IReadOnlyList<StaticWorldObject> Objects => _objects;

    public void Add(StaticWorldObject staticObject) => _objects.Add(staticObject);
}