using System.Collections.Generic;

namespace Sacred.Engine.Scene.InGame;

internal sealed partial class DoorSceneController
{
    public IEnumerable<WorldFocusTarget> FocusTargets
    {
        get
        {
            foreach (var (id, placement) in _desiredModels)
                if (_gameplay.Objects.TryGetValue(id, out var obj) && obj.CanInteract && _models.TryGetValue(id, out var model))
                    yield return new WorldFocusTarget(id, new(model.Position.X, model.Position.Y), model);
        }
    }

    public bool TryToggleFocused(uint id) => _desiredModels.TryGetValue(id, out var placement) &&
        TryToggle(placement);

    public bool HasFocusTarget(uint id) => _desiredModels.TryGetValue(id, out var placement) &&
        _gameplay.Objects.TryGetValue(id, out var obj) && obj.CanInteract && _models.ContainsKey(id);
}
