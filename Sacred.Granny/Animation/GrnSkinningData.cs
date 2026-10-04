using System.Numerics;

namespace Sacred.Granny.Animation;

/// <summary>Shared immutable bind data. Variable influence counts and raw coordinates are preserved.</summary>
public sealed class GrnSkinningData
{
    private readonly GrnBone[] _bones;
    private readonly GrnSkinningVertex[] _vertices;
    private readonly GrnSkinInfluence[] _influences;
    private readonly Matrix4x4[] _inverseBind;
    private readonly Matrix4x4[] _inverseRigidBind;
    private readonly Dictionary<string, int> _bonesByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<GrnSkinBounds> _bounds;

    internal GrnSkinningData(GrnMeshSkin skin)
    {
        _bounds = new(() => new GrnSkinBounds(this));
        _bones = (GrnBone[])skin.Skeleton.Bones.Clone();
        _vertices = new GrnSkinningVertex[skin.Vertices.Length];
        var influences = new List<GrnSkinInfluence>();
        for (var i = 0; i < _vertices.Length; i++)
        {
            var vertex = skin.Vertices[i];
            _vertices[i] = new(vertex.BindPosition, vertex.BindNormal, influences.Count,
                vertex.Weights.Length, vertex.UsesRigidBoneTransform);
            foreach (var weight in vertex.Weights) influences.Add(new(weight.BoneIndex, weight.Weight));
        }
        _influences = influences.ToArray();
        _inverseBind = new Matrix4x4[_bones.Length];
        _inverseRigidBind = new Matrix4x4[_bones.Length];
        for (var i = 0; i < _bones.Length; i++)
        {
            _inverseBind[i] = skin.GetInverseBindTransform(i);
            _inverseRigidBind[i] = skin.GetInverseRigidBindTransform(i);
            if (!string.IsNullOrWhiteSpace(_bones[i].Name)) _bonesByName.TryAdd(_bones[i].Name, i);
        }
        Projection = skin.Projection;
    }

    public ReadOnlySpan<GrnBone> Bones => _bones;
    public ReadOnlySpan<GrnSkinningVertex> Vertices => _vertices;
    public ReadOnlySpan<GrnSkinInfluence> Influences => _influences;
    public ReadOnlySpan<Matrix4x4> InverseBindTransforms => _inverseBind;
    public ReadOnlySpan<Matrix4x4> InverseRigidBindTransforms => _inverseRigidBind;
    public GrnMeshProjection Projection { get; }
    public GrnSkinBounds Bounds => _bounds.Value;
    public bool TryFindBone(string name, out int index) => _bonesByName.TryGetValue(name, out index);
    internal Matrix4x4 GetInverseBindTransform(int index) => _inverseBind[index];
    internal Matrix4x4 GetInverseRigidBindTransform(int index) => _inverseRigidBind[index];
}

public readonly record struct GrnSkinningVertex(Vector3 BindPosition, Vector3 BindNormal,
    int InfluenceOffset, int InfluenceCount, bool UsesRigidBoneTransform);
public readonly record struct GrnSkinInfluence(int BoneIndex, float Weight);
