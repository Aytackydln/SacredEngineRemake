using System.Numerics;
using Sacred.Granny.Meshes;

namespace Sacred.Granny.Animation;

/// <summary>
/// A mutable mesh instance backed by a Granny bind pose. The source asset remains immutable so
/// multiple scene instances can play the same clip independently.
/// </summary>
public sealed class GrnAnimatedMesh
{
    private readonly GrnMeshSkin _skin;
    private readonly int[] _animationBoneBySkinBone;
    private Matrix4x4[] _animationLocals = [];
    private readonly Matrix4x4[] _currentWorldTransforms;
    private readonly Matrix4x4[] _skinTransforms;
    private readonly Matrix4x4[] _rigidSkinTransforms;
    private readonly byte[] _worldTransformStates;

    public GrnAnimatedMesh(Mesh sourceMesh, GrnMeshSkin skin, GrnAnimationClip animation)
    {
        if (sourceMesh.Vertices.Length != skin.Vertices.Length)
            throw new ArgumentException("The skin vertex count does not match the source mesh.", nameof(skin));

        _skin = skin;
        Mesh = sourceMesh.CreateInstance();
        _animationBoneBySkinBone = new int[skin.Skeleton.Bones.Length];
        _currentWorldTransforms = new Matrix4x4[skin.Skeleton.Bones.Length];
        _skinTransforms = new Matrix4x4[skin.Skeleton.Bones.Length];
        _rigidSkinTransforms = new Matrix4x4[skin.Skeleton.Bones.Length];
        _worldTransformStates = new byte[skin.Skeleton.Bones.Length];
        SetAnimation(animation);
    }

    public Mesh Mesh { get; }

    public GrnAnimationClip Animation { get; private set; } = null!;

    /// <summary>Changes clips without replacing the mutable scene mesh.</summary>
    public void SetAnimation(GrnAnimationClip animation)
    {
        ArgumentNullException.ThrowIfNull(animation);

        Animation = animation;
        if (_animationLocals.Length != animation.Skeleton.Bones.Length)
            _animationLocals = new Matrix4x4[animation.Skeleton.Bones.Length];

        Array.Fill(_animationBoneBySkinBone, -1);
        for (var skinBoneIndex = 0; skinBoneIndex < _skin.Skeleton.Bones.Length; skinBoneIndex++)
        {
            var name = _skin.Skeleton.Bones[skinBoneIndex].Name;
            if (!string.IsNullOrWhiteSpace(name) && animation.Skeleton.TryFindBone(name, out var animationBoneIndex))
                _animationBoneBySkinBone[skinBoneIndex] = animationBoneIndex;
        }
    }

    /// <summary>
    /// Applies the current rigid skin transform for a named bone to a point expressed in the
    /// projected coordinate system used by <see cref="Mesh"/>.
    /// </summary>
    public bool TryTransformRigidPoint(
        string boneName,
        Vector3 projectedBindPosition,
        out Vector3 projectedPosition)
    {
        if (!_skin.Skeleton.TryFindBone(boneName, out var boneIndex))
        {
            projectedPosition = projectedBindPosition;
            return false;
        }

        var rawPosition = _skin.Projection.Unproject(projectedBindPosition);
        projectedPosition = _skin.Projection.Project(
            Vector3.Transform(rawPosition, _rigidSkinTransforms[boneIndex]));
        return true;
    }

    public bool TryTransformRigidDirection(
        string boneName,
        Vector3 projectedBindDirection,
        out Vector3 projectedDirection)
    {
        if (!_skin.Skeleton.TryFindBone(boneName, out var boneIndex))
        {
            projectedDirection = projectedBindDirection;
            return false;
        }

        var rawDirection = _skin.Projection.UnprojectDirection(projectedBindDirection);
        projectedDirection = _skin.Projection.ProjectDirection(
            Vector3.TransformNormal(rawDirection, _rigidSkinTransforms[boneIndex]));
        return true;
    }

    public void Apply(float timeSeconds)
    {
        var sampleTime = GrnAnimationSampler.WrapTime(timeSeconds, Animation.DurationSeconds);
        SampleAnimationLocals(sampleTime);

        Array.Clear(_worldTransformStates);
        for (var boneIndex = 0; boneIndex < _currentWorldTransforms.Length; boneIndex++)
            ComputeSkinBoneWorldTransform(boneIndex);

        for (var boneIndex = 0; boneIndex < _skinTransforms.Length; boneIndex++)
        {
            _skinTransforms[boneIndex] =
                _skin.GetInverseBindTransform(boneIndex) * _currentWorldTransforms[boneIndex];
            _rigidSkinTransforms[boneIndex] =
                _skin.GetInverseRigidBindTransform(boneIndex) *
                GrnRigidTransform.CreateOrOriginal(_currentWorldTransforms[boneIndex]);
        }

        var output = Mesh.Vertices;
        for (var vertexIndex = 0; vertexIndex < output.Length; vertexIndex++)
        {
            var skinVertex = _skin.Vertices[vertexIndex];
            var transformed = Vector3.Zero;
            var transformedNormal = Vector3.Zero;
            var totalWeight = 0.0f;
            foreach (var influence in skinVertex.Weights)
            {
                if ((uint)influence.BoneIndex >= (uint)_skinTransforms.Length ||
                    !float.IsFinite(influence.Weight) || influence.Weight <= 0.0f)
                    continue;

                transformed += Vector3.Transform(
                    skinVertex.BindPosition,
                    skinVertex.UsesRigidBoneTransform
                        ? _rigidSkinTransforms[influence.BoneIndex]
                        : _skinTransforms[influence.BoneIndex]) * influence.Weight;
                transformedNormal += Vector3.TransformNormal(
                    skinVertex.BindNormal,
                    skinVertex.UsesRigidBoneTransform
                        ? _rigidSkinTransforms[influence.BoneIndex]
                        : _skinTransforms[influence.BoneIndex]) * influence.Weight;
                totalWeight += influence.Weight;
            }

            var rawPosition = totalWeight > 0.000001f
                ? transformed / totalWeight
                : skinVertex.BindPosition;
            var rawNormal = totalWeight > 0.000001f
                ? transformedNormal / totalWeight
                : skinVertex.BindNormal;
            var projectedNormal = _skin.Projection.ProjectDirection(rawNormal);
            projectedNormal = projectedNormal.LengthSquared() > 0.000001f
                ? Vector3.Normalize(projectedNormal)
                : Vector3.UnitZ;
            output[vertexIndex] = output[vertexIndex] with
            {
                Position = _skin.Projection.Project(rawPosition),
                Normal = projectedNormal
            };
        }

        Mesh.MarkVerticesChanged();
    }

    private void SampleAnimationLocals(float timeSeconds)
    {
        var bones = Animation.Skeleton.Bones;
        for (var boneIndex = 0; boneIndex < bones.Length; boneIndex++)
        {
            var bone = bones[boneIndex];
            var track = boneIndex < Animation.Tracks.Length ? Animation.Tracks[boneIndex] : null;
            if (track is null)
            {
                _animationLocals[boneIndex] = bone.RestLocal;
                continue;
            }

            var translation = GrnAnimationSampler.SampleVector3(
                track.TranslationTimes,
                track.Translations,
                timeSeconds,
                bone.RestTranslation);
            var rotation = GrnAnimationSampler.SampleQuaternion(
                track.RotationTimes,
                track.Rotations,
                timeSeconds,
                bone.RestRotation);
            var scaleShear = GrnAnimationSampler.SampleMatrix(
                track.ScaleShearTimes,
                track.ScaleShears,
                timeSeconds,
                bone.RestScaleShear);
            _animationLocals[boneIndex] = GrnAnimationSampler.CreateTransform(translation, rotation, scaleShear);
        }
    }

    private bool ComputeSkinBoneWorldTransform(int boneIndex)
    {
        if (_worldTransformStates[boneIndex] == 2)
            return true;
        if (_worldTransformStates[boneIndex] == 1)
            return false;

        _worldTransformStates[boneIndex] = 1;
        var skinBone = _skin.Skeleton.Bones[boneIndex];
        var animationBoneIndex = _animationBoneBySkinBone[boneIndex];
        var local = animationBoneIndex >= 0
            ? _animationLocals[animationBoneIndex]
            : skinBone.RestLocal;
        var parentIndex = skinBone.ParentIndex;
        if (parentIndex == boneIndex)
        {
            _currentWorldTransforms[boneIndex] = local;
        }
        else if ((uint)parentIndex >= (uint)_currentWorldTransforms.Length ||
                 !ComputeSkinBoneWorldTransform(parentIndex))
        {
            _currentWorldTransforms[boneIndex] = skinBone.RestWorld;
        }
        else
        {
            _currentWorldTransforms[boneIndex] = local * _currentWorldTransforms[parentIndex];
        }

        _worldTransformStates[boneIndex] = 2;
        return true;
    }


}

