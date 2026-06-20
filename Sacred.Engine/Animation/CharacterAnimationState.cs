using Sacred.Engine.Rendering.EquipmentEffects;
using Sacred.Granny;

namespace Sacred.Engine.Animation;

internal enum CharacterAnimationStateId
{
    Idle
}

/// <summary>
/// Deliberately small first animation state machine. It owns a deformable mesh instance and keeps
/// the state boundary explicit so locomotion states and transitions can be added without changing
/// asset loading or rendering again.
/// </summary>
internal sealed class CharacterAnimationState
{
    private const float MinimumPoseIntervalSeconds = 1.0f / 240.0f;

    private readonly GrnAnimatedMesh? _animatedMesh;
    private readonly EquipmentEffectScene? _equipmentEffects;
    private float _stateTimeSeconds;
    private float _timeSinceLastPose;

    public CharacterAnimationState(
        GrnAsset asset,
        GrnAnimationClip animation,
        Mesh fallbackMesh,
        EquipmentEffectScene? equipmentEffects)
    {
        CurrentState = CharacterAnimationStateId.Idle;
        _equipmentEffects = equipmentEffects;
        if (asset.Mesh is not null && asset.Skin is not null)
        {
            _animatedMesh = new GrnAnimatedMesh(asset.Mesh, asset.Skin, animation);
            _animatedMesh.Apply(0.0f);
            Mesh = _animatedMesh.Mesh;
        }
        else
        {
            Mesh = asset.Mesh ?? fallbackMesh;
        }
    }

    public CharacterAnimationStateId CurrentState { get; private set; }

    public Mesh Mesh { get; }

    public void ApplyEquipmentEffectPose()
    {
        if (_animatedMesh is not null)
            _equipmentEffects?.ApplyPose(_animatedMesh);
    }

    public void Update(float deltaSeconds)
    {
        if (_animatedMesh is null || !float.IsFinite(deltaSeconds) || deltaSeconds <= 0.0f)
            return;

        _stateTimeSeconds += deltaSeconds;
        _timeSinceLastPose += deltaSeconds;
        if (_timeSinceLastPose < MinimumPoseIntervalSeconds)
            return;

        _timeSinceLastPose %= MinimumPoseIntervalSeconds;
        _animatedMesh.Apply(_stateTimeSeconds);
        _equipmentEffects?.ApplyPose(_animatedMesh);
    }

    public void SetState(CharacterAnimationStateId state)
    {
        if (state == CurrentState)
            return;

        CurrentState = state;
        _stateTimeSeconds = 0.0f;
        _timeSinceLastPose = 0.0f;
    }
}
