using Sacred.Granny.Animation;

namespace Sacred.Engine.Graphics.Skinning;

/// <summary>Replays the CPU evaluator's index-ordered DFS to schedule even cyclic authored graphs.</summary>
internal static class GpuPoseHierarchy
{
    public static (int[] Modes, int[] Levels) Create(GrnBone[] bones)
    {
        var state = new byte[bones.Length]; var modes = new int[bones.Length]; var levels = new int[bones.Length];
        for (var i = 0; i < bones.Length; i++) Visit(i);
        return (modes, levels);
        bool Visit(int i)
        {
            if (state[i] == 2) return true;
            if (state[i] == 1) return false;
            state[i] = 1; var parent = bones[i].ParentIndex;
            if (parent == i) modes[i] = 1;
            else if ((uint)parent >= (uint)bones.Length || !Visit(parent)) modes[i] = 0;
            else { modes[i] = 2; levels[i] = levels[parent] + 1; }
            state[i] = 2; return true;
        }
    }
}
