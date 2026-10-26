using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Sacred.Granny.Meshes;

namespace AssetViewer.Browser.Rendering;

internal sealed partial class WebGpuPreviewScene
{
    internal async Task<string> ValidateSkinningAsync()
    {
        var animation = _animated;
        if (animation is null || _skinning is null) throw new InvalidOperationException("Select an animated character first.");
        if (AnimationPlaying) throw new InvalidOperationException("Pause the character before validating its pose.");
        var pose = animation.Pose;
        var revision = pose.Revision;
        var bytes = await WebGpuInterop.ReadSkinningVerticesAsync(host);
        if (pose.Revision != revision) throw new InvalidOperationException("The character pose changed during readback.");
        var actual = MemoryMarshal.Cast<byte, VertexPositionNormalTexture>(bytes);
        if (actual.Length != animation.BindMesh.Vertices.Length) throw new InvalidDataException("GPU vertex count mismatch.");
        var positionError = 0f; var normalError = 0f; var uvError = 0f;
        for (var index = 0; index < actual.Length; index++)
        {
            var expected = animation.TransformVertex(index);
            positionError = Math.Max(positionError, MaxAbs(actual[index].Position - expected.Position));
            normalError = Math.Max(normalError, MaxAbs(actual[index].Normal - expected.Normal));
            uvError = Math.Max(uvError, Vector2.Distance(actual[index].TexCoord, expected.TexCoord));
        }
        if (!float.IsFinite(positionError + normalError + uvError)) throw new InvalidDataException("Non-finite GPU vertex output.");
        return string.Create(CultureInfo.InvariantCulture,
            $"{{\"vertices\":{actual.Length},\"maxPositionError\":{positionError},\"maxNormalError\":{normalError},\"maxUvError\":{uvError}}}");
    }

    private static float MaxAbs(Vector3 value) => Math.Max(Math.Abs(value.X), Math.Max(Math.Abs(value.Y), Math.Abs(value.Z)));
}
