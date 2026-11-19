using System.Numerics;
using Sacred.Granny.Animation;

namespace Sacred.Granny.Benchmarks;

/// <summary>Deterministic skeleton/clip fixtures shaped like a creature rig; no game data required.</summary>
internal static class SyntheticAnimation
{
    public const float KeyInterval = 1f / 30f;

    public static GrnSkeleton CreateSkeleton(int boneCount, int seed = 1)
    {
        var random = new Random(seed);
        var bones = new GrnBone[boneCount];
        for (var i = 0; i < boneCount; i++)
        {
            var parent = i == 0 ? 0 : (i - 1) / 2; // binary tree: parents precede children
            var translation = RandomVector(random) * 0.2f;
            var rotation = RandomQuaternion(random);
            var scale = Matrix4x4.Identity;
            var local = scale * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
            var world = i == 0 ? local : local * bones[parent].RestWorld;
            bones[i] = new GrnBone($"Bone{i:000}", parent, translation, rotation, scale, local, world);
        }
        return new GrnSkeleton(bones);
    }

    public static GrnSkinningData CreateSkinningData(GrnSkeleton skeleton) =>
        new GrnMeshSkin(skeleton, [], default).SkinningData;

    /// <param name="scaledBones">Fraction of bones that carry a non-identity, animated scale/shear channel.</param>
    public static GrnAnimationClip CreateClip(string name, GrnSkeleton skeleton, float seconds, int seed, float scaledBones = 0f, bool nonUniformScale = false)
    {
        var random = new Random(seed);
        var keys = Math.Max(2, (int)(seconds / KeyInterval) + 1);
        var tracks = new GrnTransformTrack?[skeleton.Bones.Length];
        for (var b = 0; b < tracks.Length; b++)
        {
            var times = new float[keys];
            var translations = new Vector3[keys];
            var rotations = new Quaternion[keys];
            for (var k = 0; k < keys; k++)
            {
                times[k] = k * KeyInterval;
                translations[k] = skeleton.Bones[b].RestTranslation + RandomVector(random) * 0.02f;
                rotations[k] = RandomQuaternion(random);
            }
            var scaled = random.NextDouble() < scaledBones;
            var scaleTimes = scaled ? (float[])times.Clone() : [0f];
            var scales = new Matrix4x4[scaled ? keys : 1];
            for (var k = 0; k < scales.Length; k++)
                scales[k] = !scaled ? Matrix4x4.Identity : nonUniformScale
                    ? Matrix4x4.CreateScale(1f + (float)random.NextDouble() * 0.5f, 1f + (float)random.NextDouble() * 0.5f, 1f + (float)random.NextDouble() * 0.5f)
                    : Matrix4x4.CreateScale(1f + (float)random.NextDouble() * 0.1f);
            tracks[b] = new GrnTransformTrack(times, translations, (float[])times.Clone(), rotations, scaleTimes, scales);
        }
        return new GrnAnimationClip(name, skeleton, tracks, (keys - 1) * KeyInterval);
    }

    public static Vector3 RandomVector(Random random) =>
        new((float)random.NextDouble() - .5f, (float)random.NextDouble() - .5f, (float)random.NextDouble() - .5f);

    public static Quaternion RandomQuaternion(Random random) => Quaternion.Normalize(new(
        (float)random.NextDouble() - .5f, (float)random.NextDouble() - .5f,
        (float)random.NextDouble() - .5f, (float)random.NextDouble() + .25f));
}
