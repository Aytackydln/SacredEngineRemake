using System;
using Sacred.Engine.Assets;
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
            _fallbackMesh = _animatedMesh.Mesh;
        }
        else
        {
            _fallbackMesh = asset.Mesh ?? fallbackMesh;
        }
    }

    public CharacterAnimationStateId CurrentState { get; private set; }

    private readonly Mesh _fallbackMesh;
    public Mesh Mesh => _animatedMesh?.Mesh ?? _fallbackMesh;
    public GrnAnimatedMesh? AnimatedMesh => _animatedMesh;
    public GrnPose? Pose => _animatedMesh?.Pose;
    private bool _materializeCpuVertices = true;
    public bool MaterializeCpuVertices
    {
        get => _animatedMesh?.MaterializeCpuVertices ?? _materializeCpuVertices;
        set
        {
            if (value && _animatedMesh?.Pose.IsEvaluated == true) _animatedMesh.MaterializeCpuMesh();
            _materializeCpuVertices = value;
            if (_animatedMesh is not null) _animatedMesh.MaterializeCpuVertices = value;
        }
    }

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
        if (_timeSinceLastPose >= MinimumPoseIntervalSeconds)
        {
            _timeSinceLastPose %= MinimumPoseIntervalSeconds;
            ApplyCurrentPose(_stateTimeSeconds);
        }
        _equipmentEffects?.ApplyPose(_animatedMesh, deltaSeconds);
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
        ApplyCurrentPose(0.0f);
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

    private void ApplyCurrentPose(float time)
    {
        _animatedMesh!.EvaluatePose(time);
        if (MaterializeCpuVertices) _animatedMesh.MaterializeCpuMesh();
    }
}
