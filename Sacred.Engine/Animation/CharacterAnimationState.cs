using System;
using Sacred.Engine.Assets;
using Sacred.Granny;
using Sacred.Granny.Animation;
using Sacred.Granny.Assets;
using Sacred.Granny.Meshes;
using Sacred.Inventory.Effects;

namespace Sacred.Engine.Animation;

internal enum CharacterAnimationStateId
{
    Idle,
    Walk,
    Run,
    Defend,
    Attack
}

/// <summary>A small immediate-transition state machine for the player character.</summary>
internal sealed class CharacterAnimationState
{
    private const float MinimumPoseIntervalSeconds = 1.0f / 240.0f;

    private readonly GrnAnimatedMesh? _animatedMesh;
    private readonly EquipmentEffectScene? _equipmentEffects;
    private readonly PlayerCharacterAnimations _animations;
    private float _stateTimeSeconds;
    private float _timeSinceLastPose;
    private CharacterAnimationStateId _locomotionState;

    public CharacterAnimationState(
        GrnAsset asset,
        PlayerCharacterAnimations animations,
        Mesh fallbackMesh,
        EquipmentEffectScene? equipmentEffects)
    {
        _animations = animations;
        _equipmentEffects = equipmentEffects;
        CurrentState = CharacterAnimationStateId.Idle;
        _locomotionState = CharacterAnimationStateId.Idle;
        if (asset.Mesh is not null && asset.Skin is not null)
        {
            _animatedMesh = new GrnAnimatedMesh(asset.Mesh, asset.Skin, animations.Idle);
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

    public void SetLocomotionState(CharacterAnimationStateId state)
    {
        if (state == CharacterAnimationStateId.Attack)
            throw new ArgumentOutOfRangeException(nameof(state));

        _locomotionState = state;
        if (CurrentState != CharacterAnimationStateId.Attack)
            SetState(state);
    }

    public void PlayAttack() => SetState(CharacterAnimationStateId.Attack, restart: true);

    public void Update(float deltaSeconds, float locomotionPlaybackSpeed = 1.0f)
    {
        if (_animatedMesh is null || !float.IsFinite(deltaSeconds) || deltaSeconds <= 0.0f)
            return;

        var animationDelta = CurrentState is CharacterAnimationStateId.Walk or CharacterAnimationStateId.Run
            ? deltaSeconds * MathF.Max(0.0f, locomotionPlaybackSpeed)
            : deltaSeconds;
        _stateTimeSeconds += animationDelta;
        if (CurrentState == CharacterAnimationStateId.Attack &&
            _stateTimeSeconds >= MathF.Max(_animations.Attack.DurationSeconds, MinimumPoseIntervalSeconds))
        {
            SetState(_locomotionState);
        }

        _timeSinceLastPose += animationDelta;
        if (_timeSinceLastPose < MinimumPoseIntervalSeconds)
            return;

        _timeSinceLastPose %= MinimumPoseIntervalSeconds;
        _animatedMesh.Apply(_stateTimeSeconds);
        _equipmentEffects?.ApplyPose(_animatedMesh, animationDelta);
    }

    private void SetState(CharacterAnimationStateId state, bool restart = false)
    {
        if (!restart && state == CurrentState)
            return;

        CurrentState = state;
        _stateTimeSeconds = 0.0f;
        _timeSinceLastPose = 0.0f;
        if (_animatedMesh is null)
            return;

        _animatedMesh.SetAnimation(AnimationFor(state));
        _animatedMesh.Apply(0.0f);
        _equipmentEffects?.ApplyPose(_animatedMesh);
    }

    private GrnAnimationClip AnimationFor(CharacterAnimationStateId state) => state switch
    {
        CharacterAnimationStateId.Idle => _animations.Idle,
        CharacterAnimationStateId.Walk => _animations.Walk,
        CharacterAnimationStateId.Run => _animations.Run,
        CharacterAnimationStateId.Defend => _animations.Defend,
        CharacterAnimationStateId.Attack => _animations.Attack,
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };
}
