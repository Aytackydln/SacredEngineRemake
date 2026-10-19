namespace Sacred.Granny.Animation;

/// <summary>Validated CPU influence ranges, shared by all instances of immutable skin data.
/// Raw file weights remain available on GrnSkinningData; summation order is preserved.</summary>
internal sealed class GrnSkinInfluenceTable
{
    internal readonly record struct Range(int Offset, int Count, float TotalWeight);
    public Range[] Ranges { get; }
    public GrnSkinInfluence[] Influences { get; }

    public GrnSkinInfluenceTable(GrnSkinningData data)
    {
        Ranges = new Range[data.Vertices.Length];
        var valid = new List<GrnSkinInfluence>(data.Influences.Length);
        for (var i = 0; i < Ranges.Length; i++)
        {
            var vertex = data.Vertices[i];
            var offset = valid.Count;
            var total = 0f;
            foreach (var influence in data.Influences.Slice(vertex.InfluenceOffset, vertex.InfluenceCount))
            {
                if ((uint)influence.BoneIndex >= (uint)data.Bones.Length ||
                    !float.IsFinite(influence.Weight) || influence.Weight <= 0) continue;
                valid.Add(influence);
                total += influence.Weight;
            }
            Ranges[i] = new(offset, valid.Count - offset, total);
        }
        Influences = valid.ToArray();
    }
}
