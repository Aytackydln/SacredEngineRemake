using System.Numerics;

namespace Sacred.Granny.Animation;

public sealed class GrnSkeleton
{
    private readonly Dictionary<string, int> _bonesByName;

    public GrnSkeleton(GrnBone[] bones)
    {
        Bones = bones;
        _bonesByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < bones.Length; index++)
        {
            if (!string.IsNullOrWhiteSpace(bones[index].Name))
                _bonesByName.TryAdd(bones[index].Name, index);
        }
    }

    public GrnBone[] Bones { get; }

    public bool TryFindBone(string name, out int boneIndex) =>
        _bonesByName.TryGetValue(name, out boneIndex);
}

public readonly record struct GrnBone(
    string Name,
    int ParentIndex,
    Vector3 RestTranslation,
    Quaternion RestRotation,
    Matrix4x4 RestScaleShear,
    Matrix4x4 RestLocal,
    Matrix4x4 RestWorld);
