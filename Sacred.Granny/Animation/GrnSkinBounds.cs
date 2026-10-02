using System.Numerics;

namespace Sacred.Granny.Animation;

/// <summary>Immutable bind envelopes: positive normalized weights keep a vertex inside the union
/// of its transformed bone envelopes. This is conservative, unlike the Demo's two-endpoint cache.</summary>
public sealed class GrnSkinBounds
{
    private struct Box
    {
        public bool HasPoints;
        public bool Invalid;
        public Vector3 Min, Max;
        public void Add(Vector3 point)
        {
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z)) Invalid = true;
            if (!HasPoints) { Min = Max = point; HasPoints = true; }
            else { Min = Vector3.Min(Min, point); Max = Vector3.Max(Max, point); }
        }
    }
    private readonly Box[] _regular, _rigid;
    private Box _fallback;
    private readonly GrnSkinningData _data;
    private readonly float _roundingFactor;
    internal GrnSkinBounds(GrnSkinningData data)
    {
        _data = data;
        var maximumInfluences = 0;
        _regular = new Box[data.Bones.Length]; _rigid = new Box[data.Bones.Length];
        foreach (var vertex in data.Vertices)
        {
            maximumInfluences = Math.Max(maximumInfluences, vertex.InfluenceCount);
            var weights = data.Influences.Slice(vertex.InfluenceOffset, vertex.InfluenceCount);
            var total = 0f;
            foreach (var weight in weights)
                if ((uint)weight.BoneIndex < (uint)_regular.Length && float.IsFinite(weight.Weight) && weight.Weight > 0) total += weight.Weight;
            if (total <= .000001f) { _fallback.Add(vertex.BindPosition); continue; }
            // A nonfinite weight sum cannot provide reliable spatial bounds. Fail open.
            if (!float.IsFinite(total)) { _fallback.Add(new(float.NaN)); continue; }
            foreach (var weight in weights)
            {
                if ((uint)weight.BoneIndex >= (uint)_regular.Length || !float.IsFinite(weight.Weight) || weight.Weight <= 0) continue;
                if (vertex.UsesRigidBoneTransform) _rigid[weight.BoneIndex].Add(vertex.BindPosition);
                else _regular[weight.BoneIndex].Add(vertex.BindPosition);
            }
        }
        _roundingFactor = Math.Max(.00001f, maximumInfluences * .0000008f);
    }
    public (Vector3 Minimum, Vector3 Maximum) Evaluate(GrnPose pose)
    {
        if (!ReferenceEquals(pose.Data, _data) || !pose.IsEvaluated) throw new ArgumentException("An evaluated matching pose is required.", nameof(pose));
        var bounds = new Box();
        Append(ref bounds, _fallback, Matrix4x4.Identity);
        for (var i = 0; i < _regular.Length; i++)
        {
            Append(ref bounds, _regular[i], pose.SkinTransforms[i]);
            Append(ref bounds, _rigid[i], pose.RigidSkinTransforms[i]);
        }
        if (bounds.Invalid) return (new(float.NaN), new(float.NaN));
        if (!bounds.HasPoints) return (Vector3.Zero, Vector3.Zero);
        // Outward slack accounts for floating-point weighted accumulation and division.
        var padding = Vector3.Max(Vector3.One, Vector3.Max(Vector3.Abs(bounds.Min), Vector3.Abs(bounds.Max))) * _roundingFactor;
        return (bounds.Min - padding, bounds.Max + padding);
    }
    private void Append(ref Box result, Box source, Matrix4x4 transform)
    {
        if (!source.HasPoints) return;
        if (source.Invalid) { result.Invalid = true; return; }
        for (var corner = 0; corner < 8; corner++)
        {
            var point = new Vector3((corner & 1) == 0 ? source.Min.X : source.Max.X,
                (corner & 2) == 0 ? source.Min.Y : source.Max.Y, (corner & 4) == 0 ? source.Min.Z : source.Max.Z);
            result.Add(_data.Projection.Project(Vector3.Transform(point, transform)));
        }
    }
}
