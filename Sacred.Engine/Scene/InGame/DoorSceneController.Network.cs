using Sacred.GameplayHost.World;

namespace Sacred.Engine.Scene.InGame;

internal sealed partial class DoorSceneController
{
    private readonly IGameplayObjectWorld _offlineObjects;
    private IGameplayObjectWorld _gameplay;
    private long _gameplayRevision = -1;
    public string NetworkInteractionStatus => _gameplay.InteractionStatus;

    public void BindGameplayWorld(IGameplayObjectWorld? world)
    {
        _gameplay = world ?? _offlineObjects;
        _gameplayRevision = -1;
        _desiredModels.Clear(); _models.Clear(); _doorAnimations.Clear(); _loads.Clear(); _failedLoads.Clear();
        _instances.Clear(); _animationStates.Clear();
        PublishModels();
        EngineLog.WriteLine(world is null ? "World objects returned to offline gameplay." : "World objects follow the gameplay session interface.");
    }

    private bool NetworkObjectOpen(uint reference) => _gameplay.Objects.TryGetValue(reference, out var obj) && obj.IsActivated;

    private bool TryRequestGameplayInteraction(WorldModelPlacement target)
    {
        if (!_models.TryGetValue(target.StaticObject.StaticId, out var model)) return false;
        _doorAnimations.TryGetValue(target.StaticObject.StaticId, out var playback);
        var handled = _gameplay.RequestInteraction(target.StaticObject.StaticId,
            new(_playerPosition(), _scene.Indoor.ActiveGroup?.SurfaceLevel ?? 0,
                WorldObjectInteractionReach.Contains(model, _playerPosition()), playback?.CanActivate == true, playback?.CanReset == true));
        EngineLog.WriteLine("World interaction: " + _gameplay.InteractionStatus + ".");
        return handled;
    }
}
