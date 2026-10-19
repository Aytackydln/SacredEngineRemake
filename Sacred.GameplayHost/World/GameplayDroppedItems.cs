using System.Numerics;
using Sacred.Core.Pak.Items;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.World;

internal sealed class GameplayDroppedItems
{
    private readonly Dictionary<uint, GameplayDroppedItemPlayback> _items = [];
    public static bool IsDroppedItem(GoldReplicatedObject obj, ItemsPakEntry item) =>
        obj.Creature is null && obj.Item is { InventoryId: 0 } && obj.Physical is not null &&
        (obj.BaseState.Flags & 0x413) == 0 && !GameplayObjectPlacement.IsModelProp(item) &&
        item.ModelDesc.Category is not (SacredItemCategory.Creature or SacredItemCategory.Projectile or SacredItemCategory.Effect);

    public void Synchronize(Dictionary<uint, GameplayWorldObject> objects, Func<Vector2, float> height)
    {
        foreach (var reference in _items.Keys.Where(id => !objects.TryGetValue(id, out var obj) || obj.Drop is null).ToArray())
            _items.Remove(reference);
        foreach (var (reference, obj) in objects.ToArray())
        {
            if (obj.Drop is null || obj.Replica is not { } replica) continue;
            if (!_items.TryGetValue(reference, out var playback)) _items[reference] = playback = new();
            playback.Synchronize(replica, height);
            var pose = playback.Pose;
            objects[reference] = obj with { Drop = pose, Position = new(pose.Position, pose.Height) };
        }
    }
    public void Advance(Dictionary<uint, GameplayWorldObject> objects, Func<Vector2, float> height, float seconds)
    {
        foreach (var (reference, playback) in _items)
        {
            if (!playback.Pose.IsFlying) continue;
            playback.Advance(seconds, height);
            var pose = playback.Pose;
            objects[reference] = objects[reference] with { Drop = pose, Position = new(pose.Position, pose.Height) };
        }
    }
    public void Clear() => _items.Clear();
}
