using System.Numerics;

namespace Sacred.Granny.Animation;

/// <summary>Samples a named animation helper without requiring a renderable mesh.</summary>
public static class GrnAnimationBoneSampler
{
    public static Vector3 SamplePosition(GrnAnimationClip clip, string boneName, float seconds)
    {
        if (!clip.Skeleton.TryFindBone(boneName, out var index))
            throw new ArgumentException($"Animation {clip.Name} has no bone {boneName}.", nameof(boneName));
        var world = Matrix4x4.Identity;
        var time = Math.Clamp(seconds, 0, clip.DurationSeconds);
        for (var depth = 0; depth < clip.Skeleton.Bones.Length; depth++)
        {
            var bone = clip.Skeleton.Bones[index];
            var track = clip.Tracks[index];
            var local = track is null ? bone.RestLocal : GrnAnimationSampler.CreateTransform(
                GrnAnimationSampler.SampleVector3(track.TranslationTimes, track.Translations, time, bone.RestTranslation),
                GrnAnimationSampler.SampleQuaternion(track.RotationTimes, track.Rotations, time, bone.RestRotation),
                GrnAnimationSampler.SampleMatrix(track.ScaleShearTimes, track.ScaleShears, time, bone.RestScaleShear));
            world *= local;
            if (bone.ParentIndex < 0 || bone.ParentIndex == index) return world.Translation;
            index = bone.ParentIndex;
        }
        throw new InvalidDataException($"Animation {clip.Name} has a cyclic skeleton.");
    }
}
