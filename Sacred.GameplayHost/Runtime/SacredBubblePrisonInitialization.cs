using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Runtime;

/// <summary>Initial ring angles, angular velocities and heights. PC=24 rounds
/// each original x87 instruction. The tenth ring is overwritten after the loop,
/// retaining all thirty loop draws plus two final draws in the shared sequence.</summary>
public static class SacredBubblePrisonInitialization
{
    public static (float[] Angles, float[] AngularVelocities, float[] Heights) Create(
        SacredBubblePrisonParameters parameters, SacredRandomSequence random)
    {
        var arithmetic = new SacredResultArithmetic(SacredX87Precision.Single);
        var angles = new float[10];
        var velocities = new float[10];
        var heights = new float[10];
        for (var index = 0; index < angles.Length; index++)
        {
            angles[index] = (float)arithmetic.Multiply(Draw(), parameters.FullTurn);
            velocities[index] = (float)arithmetic.Add(arithmetic.Multiply(Draw(), parameters.AngularVelocityScale), parameters.AngularVelocityBias);
            var sampledHeight = Draw();
            heights[index] = (float)arithmetic.Multiply(sampledHeight, arithmetic.Multiply(index, parameters.HeightStep));
        }
        angles[^1] = (float)arithmetic.Multiply(Draw(), parameters.FullTurn);
        velocities[^1] = (float)arithmetic.Add(arithmetic.Multiply(Draw(), parameters.LastAngularVelocityScale), parameters.LastAngularVelocityBias);
        heights[^1] = parameters.LastHeight;
        return (angles, velocities, heights);
        double Draw() => arithmetic.Multiply(random.Next(), parameters.RandomScale);
    }
}
