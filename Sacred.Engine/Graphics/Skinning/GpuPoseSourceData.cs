using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Sacred.Granny.Animation;
using Sacred.Shaders;

namespace Sacred.Engine.Graphics.Skinning;

/// <summary>Immutable preparation snapshot. All name resolution and hierarchy repair happen once.</summary>
internal sealed class GpuPoseSourceData
{
    public byte[] Bytes { get; }
    public int AnimationCount { get; }
    public int BoneCount { get; }
    public int SkinOffset { get; }
    public int MaximumLevel { get; }
    public float Duration { get; }
    public int WorldOffset => AnimationCount * 64;
    public int PaletteOffset => WorldOffset + Math.Max(1, BoneCount) * 64;
    public int PaletteStride => Math.Max(1, BoneCount) * 64;
    public int OutputBytes => PaletteOffset + PaletteStride * 2;

    public GpuPoseSourceData(GrnSkinningData skin, GrnAnimationClip clip)
    {
        AnimationCount = clip.Skeleton.Bones.Length; BoneCount = skin.Bones.Length;
        Duration = clip.DurationSeconds;
        SkinOffset = AnimationCount * PoseGpuShaderLayout.AnimationBoneBytes;
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        stream.SetLength(Math.Max(16, SkinOffset + BoneCount * PoseGpuShaderLayout.SkinBoneBytes));
        var skinBones = skin.Bones.ToArray();
        var (modes, levels) = GpuPoseHierarchy.Create(skinBones);
        for (var i = 0; i < AnimationCount; i++)
        {
            var bone = clip.Skeleton.Bones[i]; var track = i < clip.Tracks.Length ? clip.Tracks[i] : null;
            var position = i * PoseGpuShaderLayout.AnimationBoneBytes;
            Range(position, track?.TranslationTimes ?? [], track?.Translations ?? []);
            Range(position + 16, track?.RotationTimes ?? [], GpuPoseRotationKey.Prepare(track?.Rotations ?? []));
            Range(position + 32, track?.ScaleShearTimes ?? [], track?.ScaleShears ?? []);
            stream.Position = position + 48;
            Vector(new(bone.RestTranslation, 0)); var restRotation = GpuPoseRotationKey.Normalize(bone.RestRotation);
            Vector(new(restRotation.X, restRotation.Y, restRotation.Z, restRotation.W));
            Matrix(bone.RestScaleShear); Matrix(bone.RestLocal);
            writer.Write(track is null ? 0 : 1); writer.Write(0); writer.Write(0); writer.Write(0);
        }
        for (var i = 0; i < BoneCount; i++)
        {
            var bone = skinBones[i]; var mapped = -1;
            if (string.IsNullOrWhiteSpace(bone.Name) || !clip.Skeleton.TryFindBone(bone.Name, out mapped)) mapped = -1;
            stream.Position = SkinOffset + i * PoseGpuShaderLayout.SkinBoneBytes;
            writer.Write(mapped); writer.Write(bone.ParentIndex); writer.Write(modes[i]); writer.Write(levels[i]);
            Matrix(bone.RestLocal); Matrix(bone.RestWorld);
            Matrix(skin.InverseBindTransforms[i]); Matrix(skin.InverseRigidBindTransforms[i]);
            MaximumLevel = Math.Max(MaximumLevel, levels[i]);
        }
        Bytes = stream.ToArray();


        void Vector(Vector4 v) { writer.Write(v.X); writer.Write(v.Y); writer.Write(v.Z); writer.Write(v.W); }
        void Matrix(Matrix4x4 m)
        {
            Vector(new(m.M11, m.M12, m.M13, m.M14)); Vector(new(m.M21, m.M22, m.M23, m.M24));
            Vector(new(m.M31, m.M32, m.M33, m.M34)); Vector(new(m.M41, m.M42, m.M43, m.M44));
        }
        void Range<T>(int header, float[] times, T[] values) where T : unmanaged
        {
            stream.Position = stream.Length; var timeOffset = checked((int)stream.Position);
            foreach (var t in times) writer.Write(t);
            // Root buffer views have no descriptor bounds. Give empty ranges physical
            // storage so speculative shader loads cannot address beyond the allocation.
            if (times.Length == 0) writer.Write(0f);
            var valueOffset = checked((int)stream.Position);
            writer.Write(MemoryMarshal.AsBytes(values.AsSpan()));
            if (values.Length == 0) writer.Write(new byte[Marshal.SizeOf<T>()]);
            stream.Position = header;
            writer.Write(timeOffset); writer.Write(valueOffset); writer.Write(times.Length); writer.Write(values.Length);
        }
    }
}
