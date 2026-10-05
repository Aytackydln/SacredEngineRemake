using System.Collections.Generic;
using Sacred.World.Geometry;

namespace Sacred.Engine.Scene.InGame;

internal sealed partial class DoorSceneController
{
    public IEnumerable<WorldFocusTarget> FocusTargets
    {
        get
        {
            foreach (var (id, placement) in _desiredModels)
                if (placement.Item.ModelDesc.IsSelectable && _models.TryGetValue(id, out var model))
                    yield return new WorldFocusTarget(id, WorldModelPose.TilePosition(placement.StaticObject), model);
        }
    }

    public bool TryToggleFocused(uint id) => _desiredModels.TryGetValue(id, out var placement) &&
        TryToggle(placement);

    public bool HasFocusTarget(uint id) => _desiredModels.TryGetValue(id, out var placement) &&
        placement.Item.ModelDesc.IsSelectable && _models.ContainsKey(id);
}
