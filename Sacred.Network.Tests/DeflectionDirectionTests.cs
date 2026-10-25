using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Particles;

namespace Sacred.Network.Tests;

[TestClass, TestCategory("Packet")]
public sealed class DeflectionDirectionTests
{
    [TestMethod]
    public void BurstsCoverEveryProjectedDirectionAndRetainTheirShapeSpeedAndSeed()
    {
        var definition = SacredDeflectionFxDefinition.Original;
        var projection = SacredParticleCatalogue.LoadEmbedded().Projection;
        var quadrants = new int[4];
        for (var seed = 0; seed < 128; seed++)
        {
            var random = new Random(seed); var repeat = new Random(seed);
            var burst = SacredDeflectionFxPlayback.InitializeBurst(definition, () => random.Next(32768));
            var replay = SacredDeflectionFxPlayback.InitializeBurst(definition, () => repeat.Next(32768));
            CollectionAssert.AreEqual(burst.Positions, replay.Positions);
            CollectionAssert.AreEqual(burst.Velocities, replay.Velocities);
            var mean = Vector3.Zero;
            for (var streak = 0; streak < definition.StreakCount; streak++)
            {
                var index = streak * definition.TrailPoints;
                var velocity = burst.Velocities[index]; mean += velocity;
                Assert.AreEqual(definition.Speed, velocity.Length(), .001f);
                for (var point = 1; point < definition.TrailPoints; point++)
                {
                    Assert.AreEqual(velocity, burst.Velocities[index + point]);
                    Assert.AreEqual(definition.TrailSpacing,
                        Vector3.Distance(burst.Positions[index + point - 1], burst.Positions[index + point]), .0001f);
                }
            }
            var screen = projection.Project(mean);
            quadrants[(screen.X >= 0 ? 1 : 0) | (screen.Y >= 0 ? 2 : 0)]++;
        }
        Assert.IsTrue(quadrants.All(count => count >= 16), string.Join(", ", quadrants));
    }
}
