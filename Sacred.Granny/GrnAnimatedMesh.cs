using System.Numerics;

namespace Sacred.Granny;

/// <summary>
/// A mutable mesh instance backed by a Granny bind pose. The source asset remains immutable so
/// multiple scene instances can play the same clip independently.
/// </summary>
public sealed class GrnAnimatedMesh
{
    private readonly GrnMeshSkin _skin;
    private readonly int[] _animationBoneBySkinBone;
    private readonly Matrix4x4[] _animationLocals;
    private readonly Matrix4x4[] _currentWorldTransforms;
    private readonly Matrix4x4[] _skinTransforms;
    private readonly Matrix4x4[] _rigidSkinTransforms;
    private readonly byte[] _worldTransformStates;

    public GrnAnimatedMesh(Mesh sourceMesh, GrnMeshSkin skin, GrnAnimationClip animation)
    {
        if (sourceMesh.Vertices.Length != skin.Vertices.Length)
            throw new ArgumentException("The skin vertex count does not match the source mesh.", nameof(skin));

        _skin = skin;
        Animation = animation;
        Mesh = sourceMesh.CreateInstance();
        _animationBoneBySkinBone = new int[skin.Skeleton.Bones.Length];
        Array.Fill(_animationBoneBySkinBone, -1);
        for (var skinBoneIndex = 0; skinBoneIndex < skin.Skeleton.Bones.Length; skinBoneIndex++)
        {
            var name = skin.Skeleton.Bones[skinBoneIndex].Name;
            if (!string.IsNullOrWhiteSpace(name) && animation.Skeleton.TryFindBone(name, out var animationBoneIndex))
                _animationBoneBySkinBone[skinBoneIndex] = animationBoneIndex;
        }

        _animationLocals = new Matrix4x4[animation.Skeleton.Bones.Length];
        _currentWorldTransforms = new Matrix4x4[skin.Skeleton.Bones.Length];
        _skinTransforms = new Matrix4x4[skin.Skeleton.Bones.Length];
        _rigidSkinTransforms = new Matrix4x4[skin.Skeleton.Bones.Length];
        _worldTransformStates = new byte[skin.Skeleton.Bones.Length];
    }

    public Mesh Mesh { get; }

    public GrnAnimationClip Animation { get; }

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
        var sampleTime = WrapTime(timeSeconds, Animation.DurationSeconds);
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

            var translation = SampleVector3(
                track.TranslationTimes,
                track.Translations,
                timeSeconds,
                bone.RestTranslation);
            var rotation = SampleQuaternion(
                track.RotationTimes,
                track.Rotations,
                timeSeconds,
                bone.RestRotation);
            var scaleShear = SampleMatrix(
                track.ScaleShearTimes,
                track.ScaleShears,
                timeSeconds,
                bone.RestScaleShear);
            _animationLocals[boneIndex] = CreateTransform(translation, rotation, scaleShear);
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

    private static float WrapTime(float timeSeconds, float durationSeconds)
    {
        if (!float.IsFinite(timeSeconds) || !float.IsFinite(durationSeconds) || durationSeconds <= 0.000001f)
            return 0.0f;

        var wrapped = timeSeconds % durationSeconds;
        return wrapped < 0.0f ? wrapped + durationSeconds : wrapped;
    }

    private static Vector3 SampleVector3(
        IReadOnlyList<float> times,
        IReadOnlyList<Vector3> values,
        float time,
        Vector3 fallback)
    {
        if (values.Count == 0)
            return fallback;
        var (left, right, amount) = FindKeys(times, values.Count, time);
        return left == right ? values[left] : Vector3.Lerp(values[left], values[right], amount);
    }

    private static Quaternion SampleQuaternion(
        IReadOnlyList<float> times,
        IReadOnlyList<Quaternion> values,
        float time,
        Quaternion fallback)
    {
        if (values.Count == 0)
            return NormalizeOrIdentity(fallback);
        var (left, right, amount) = FindKeys(times, values.Count, time);
        var first = NormalizeOrIdentity(values[left]);
        if (left == right)
            return first;
        return Quaternion.Normalize(Quaternion.Slerp(first, NormalizeOrIdentity(values[right]), amount));
    }

    private static Matrix4x4 SampleMatrix(
        IReadOnlyList<float> times,
        IReadOnlyList<Matrix4x4> values,
        float time,
        Matrix4x4 fallback)
    {
        if (values.Count == 0)
            return fallback;
        var (left, right, amount) = FindKeys(times, values.Count, time);
        return left == right ? values[left] : Lerp(values[left], values[right], amount);
    }

    private static (int Left, int Right, float Amount) FindKeys(
        IReadOnlyList<float> times,
        int valueCount,
        float time)
    {
        var count = Math.Min(times.Count, valueCount);
        if (count <= 1 || time <= times[0])
            return (0, 0, 0.0f);
        if (time >= times[count - 1])
            return (count - 1, count - 1, 0.0f);

        var lower = 0;
        var upper = count - 1;
        while (upper - lower > 1)
        {
            var middle = lower + (upper - lower) / 2;
            if (times[middle] <= time)
                lower = middle;
            else
                upper = middle;
        }

        var span = times[upper] - times[lower];
        var amount = float.IsFinite(span) && span > 0.000001f
            ? Math.Clamp((time - times[lower]) / span, 0.0f, 1.0f)
            : 0.0f;
        return (lower, upper, amount);
    }

    private static Matrix4x4 CreateTransform(
        Vector3 translation,
        Quaternion rotation,
        Matrix4x4 scaleShear) =>
        scaleShear *
        Matrix4x4.CreateFromQuaternion(NormalizeOrIdentity(rotation)) *
        Matrix4x4.CreateTranslation(translation);

    private static Quaternion NormalizeOrIdentity(Quaternion value) =>
        value.LengthSquared() > 0.000001f &&
        float.IsFinite(value.X) && float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) && float.IsFinite(value.W)
            ? Quaternion.Normalize(value)
            : Quaternion.Identity;

    private static Matrix4x4 Lerp(Matrix4x4 left, Matrix4x4 right, float amount) => new(
        float.Lerp(left.M11, right.M11, amount), float.Lerp(left.M12, right.M12, amount), float.Lerp(left.M13, right.M13, amount), 0.0f,
        float.Lerp(left.M21, right.M21, amount), float.Lerp(left.M22, right.M22, amount), float.Lerp(left.M23, right.M23, amount), 0.0f,
        float.Lerp(left.M31, right.M31, amount), float.Lerp(left.M32, right.M32, amount), float.Lerp(left.M33, right.M33, amount), 0.0f,
        0.0f, 0.0f, 0.0f, 1.0f);
}
