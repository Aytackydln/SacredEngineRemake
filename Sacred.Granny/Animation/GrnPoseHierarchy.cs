namespace Sacred.Granny.Animation;

/// <summary>Compiles the reference traversal once, including its invalid-parent and cycle fallbacks.</summary>
internal static class GrnPoseHierarchy
{
    internal readonly record struct Step(int Bone, bool RestWorld);

    public static Step[] Compile(GrnSkinningData data)
    {
        var states = new byte[data.Bones.Length];
        var steps = new List<Step>(states.Length);
        for (var i = 0; i < states.Length; i++) Visit(i);
        return steps.ToArray();

        bool Visit(int index)
        {
            if (states[index] == 2) return true;
            if (states[index] == 1) return false;
            states[index] = 1;
            var parent = data.Bones[index].ParentIndex;
            var rest = parent != index && ((uint)parent >= (uint)states.Length || !Visit(parent));
            steps.Add(new(index, rest));
            states[index] = 2;
            return true;
        }
    }
}
